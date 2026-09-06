using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tutor365.Domain.Enums;
using Tutor365.Infrastructure.Data;
using Tutor365.Infrastructure.Services;
using Xunit;

namespace Tutor365.IntegrationTests;

public class AccountAndAlertTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;
    public AccountAndAlertTests(ApiFactory factory) { _factory = factory; _client = factory.CreateClient(); }

    private async Task<string> ForceOtpAsync(string email, OtpPurpose purpose)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = email.ToUpperInvariant();
        var otp = db.OtpCodes.Where(o => o.Email == normalized && o.Purpose == purpose && o.ConsumedAt == null).OrderByDescending(o => o.CreatedAt).First();
        const string code = "123456";
        otp.CodeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{normalized}:{code}")));
        await db.SaveChangesAsync();
        return code;
    }

    private async Task<(string ParentToken, string ChildToken, string StudentId, string ParentEmail, string ChildEmail)> SetupFamilyAsync()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var parentEmail = $"it-acct-parent-{stamp}@tutor365.test";
        var childEmail = $"it-acct-child-{stamp}@tutor365.test";
        var (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email = parentEmail, password = "Parent1234", firstName = "Alert", lastName = "Parent" });
        s.Should().Be(200);
        var code = await ForceOtpAsync(parentEmail, OtpPurpose.Registration);
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { email = parentEmail, code });
        s.Should().Be(200);
        var parentToken = b.Data().Str("accessToken");
        var (_, years) = await _client.SendAsync(HttpMethod.Get, "/api/v1/year-groups");
        var year10 = years.Data().EnumerateArray().First(y => y.GetProperty("number").GetInt32() == 10).Str("id");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/parents/me/children", new { firstName = "Robin", lastName = "Test", email = childEmail, password = "Child1234", yearGroupId = year10, targetGrade = 6 }, parentToken);
        s.Should().Be(201, b.ToString());
        var studentId = b.Data().GetProperty("summary").Str("studentId");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = childEmail, password = "Child1234" });
        s.Should().Be(200);
        return (parentToken, b.Data().Str("accessToken"), studentId, parentEmail, childEmail);
    }

    [Fact]
    public async Task Student_can_update_profile_change_email_with_code_and_sign_out_everywhere()
    {
        var (parentToken, childToken, studentId, _, childEmail) = await SetupFamilyAsync();

        // Profile
        var (s, b) = await _client.SendAsync(HttpMethod.Put, "/api/v1/auth/me", new { firstName = "Robyn", avatarUrl = "🦊", timeZone = "Europe/Dublin", schoolName = "Oakwood" }, childToken);
        s.Should().Be(200, b.ToString());
        b.Data().GetProperty("user").Str("firstName").Should().Be("Robyn");
        b.Data().GetProperty("user").Str("avatarUrl").Should().Be("🦊");
        b.Data().GetProperty("user").Str("timeZone").Should().Be("Europe/Dublin");
        b.Data().GetProperty("student").Str("schoolName").Should().Be("Oakwood");
        (s, b) = await _client.SendAsync(HttpMethod.Put, "/api/v1/auth/me", new { firstName = "   " }, childToken);
        s.Should().Be(400, "blank names are rejected");

        // Email change: wrong password, then real flow, then the new address works for login
        var newEmail = childEmail.Replace("it-acct-child", "it-acct-child-new");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-email/request", new { newEmail, currentPassword = "Nope12345" }, childToken);
        s.Should().Be(422); b.Str("errorCode").Should().Be("INVALID_CURRENT_PASSWORD");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-email/request", new { newEmail, currentPassword = "Child1234" }, childToken);
        s.Should().Be(200, b.ToString());
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-email/confirm", new { newEmail, code = "000000" }, childToken);
        s.Should().Be(422); b.Str("errorCode").Should().Be("INVALID_OTP");
        var code = await ForceOtpAsync(newEmail, OtpPurpose.EmailChange);
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-email/confirm", new { newEmail, code }, childToken);
        s.Should().Be(200, b.ToString());
        b.Data().GetProperty("user").Str("email").Should().Be(newEmail);
        (s, _) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = childEmail, password = "Child1234" });
        s.Should().Be(401, "old address no longer signs in");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = newEmail, password = "Child1234" });
        s.Should().Be(200);
        var refresh = b.Data().Str("refreshToken");

        // Sessions + logout-all
        (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/auth/sessions", token: childToken);
        s.Should().Be(200); b.Data().GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        (s, _) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all", token: childToken);
        s.Should().Be(200);
        (s, _) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", new { refreshToken = refresh });
        s.Should().Be(401, "every refresh token was revoked");

        await _client.SendAsync(HttpMethod.Delete, $"/api/v1/parents/me/children/{studentId}", token: parentToken);
    }

    [Fact]
    public async Task Maintenance_alerts_parents_about_missed_days_and_overdue_work()
    {
        var (parentToken, childToken, studentId, _, _) = await SetupFamilyAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        // Today's slots, shifted to yesterday so they count as missed.
        var (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/students/me/today/regenerate", token: childToken);
        s.Should().Be(200, b.ToString());
        // Assigned work due yesterday.
        var (_, subjects) = await _client.SendAsync(HttpMethod.Get, "/api/v1/subjects");
        var maths = subjects.Data().EnumerateArray().First(x => x.Str("code") == "MAT").Str("id");
        (s, b) = await _client.SendAsync(HttpMethod.Post, $"/api/v1/parents/me/children/{studentId}/study-plans",
            new { title = "Catch-up mock", items = new[] { new { subjectId = maths, itemType = "MockExam", dueDate = yesterday.ToString("yyyy-MM-dd") } } }, parentToken);
        s.Should().Be(201, b.ToString());

        var sid = Guid.Parse(studentId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slots = await db.DailyStudySlots.Where(x => x.StudentId == sid).ToListAsync();
            slots.Should().NotBeEmpty();
            foreach (var slot in slots) slot.Date = yesterday;
            await db.SaveChangesAsync();
        }

        var maintenance = _factory.Services.GetServices<IHostedService>().OfType<MaintenanceHostedService>().Single();
        await maintenance.RunOnceAsync(CancellationToken.None);

        (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/notifications?pageSize=50", token: parentToken);
        s.Should().Be(200);
        var titles = b.Data().GetProperty("items").EnumerateArray().Select(n => n.Str("title")).ToList();
        titles.Should().Contain(t => t.Contains("missed yesterday's study sessions"));
        titles.Should().Contain(t => t.Contains("has overdue assigned work"));
        (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/notifications?pageSize=50", token: childToken);
        b.Data().GetProperty("items").EnumerateArray().Select(n => n.Str("title")).Should().Contain("Assigned work is overdue");

        // Running again must not duplicate the alerts.
        await maintenance.RunOnceAsync(CancellationToken.None);
        (_, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/notifications?pageSize=50", token: parentToken);
        b.Data().GetProperty("items").EnumerateArray().Count(n => n.Str("title").Contains("missed yesterday")).Should().Be(1);

        await _client.SendAsync(HttpMethod.Delete, $"/api/v1/parents/me/children/{studentId}", token: parentToken);
    }
}
