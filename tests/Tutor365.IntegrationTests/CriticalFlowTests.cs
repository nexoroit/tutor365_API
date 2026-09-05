using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Tutor365.IntegrationTests;

/// <summary>Critical workflows: parent registers with OTP, creates child, child studies a lesson (start, answer, pause, resume, complete), parent sees progress, role boundaries hold.</summary>
public class CriticalFlowTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;
    public CriticalFlowTests(ApiFactory factory) { _factory = factory; _client = factory.CreateClient(); }

    private async Task<string> GetOtpAsync(string email, int purpose)
    {
        // Read the latest OTP hash is not possible; instead use the seeded dev path: the API logs OTPs when SMTP is disabled.
        // For tests we bypass by inserting a known code through the OtpService contract.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tutor365.Infrastructure.Data.AppDbContext>();
        var normalized = email.ToUpperInvariant();
        var otp = db.OtpCodes.Where(o => o.Email == normalized && o.Purpose == (Tutor365.Domain.Enums.OtpPurpose)purpose && o.ConsumedAt == null).OrderByDescending(o => o.CreatedAt).First();
        const string code = "123456";
        otp.CodeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{normalized}:{code}")));
        await db.SaveChangesAsync();
        return code;
    }

    [Fact]
    public async Task Parent_registers_creates_child_and_child_completes_a_lesson()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var parentEmail = $"it-parent-{stamp}@tutor365.test";
        var childEmail = $"it-child-{stamp}@tutor365.test";

        // Register + verify
        var (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email = parentEmail, password = "Parent1234", firstName = "Int", lastName = "Parent" });
        s.Should().Be(200); b.GetProperty("success").GetBoolean().Should().BeTrue();
        (s, _) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = parentEmail, password = "Parent1234" });
        s.Should().Be(401, "email is not verified yet");
        var code = await GetOtpAsync(parentEmail, 1);
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { email = parentEmail, code });
        s.Should().Be(200);
        var parentToken = b.Data().Str("accessToken");
        b.Data().GetProperty("user").Str("role").Should().Be("Parent");

        // Create child
        var (_, years) = await _client.SendAsync(HttpMethod.Get, "/api/v1/year-groups");
        var year10 = years.Data().EnumerateArray().First(y => y.GetProperty("number").GetInt32() == 10).Str("id");
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/parents/me/children", new { firstName = "Kid", lastName = "Test", email = childEmail, password = "Child1234", yearGroupId = year10, targetGrade = 6 }, parentToken);
        s.Should().Be(201);
        var studentId = b.Data().GetProperty("summary").Str("studentId");

        // Child login and dashboard
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = childEmail, password = "Child1234" });
        s.Should().Be(200);
        var childToken = b.Data().Str("accessToken");
        (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/students/me/dashboard", token: childToken);
        s.Should().Be(200);
        var recommended = b.Data().GetProperty("recommended");
        recommended.ValueKind.Should().NotBe(JsonValueKind.Null, "content must be imported for recommendations");
        var lessonId = recommended.Str("lessonId");

        // Start session, answer every question, pause, resume, complete
        (s, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/study-sessions", new { lessonId }, childToken);
        s.Should().Be(200);
        var session = b.Data();
        var sessionId = session.Str("id");
        foreach (var activity in session.GetProperty("activities").EnumerateArray())
        {
            if (activity.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.Object)
            {
                var type = q.Str("questionType");
                object body = type is "MultipleChoice" or "TrueFalse" or "MultipleAnswer"
                    ? new { questionId = q.Str("id"), answerJson = new { selectedOptionIds = new[] { q.GetProperty("options")[0].Str("id") } }, timeSpentSeconds = 5 }
                    : new { questionId = q.Str("id"), answerText = "42", timeSpentSeconds = 5 };
                (s, b) = await _client.SendAsync(HttpMethod.Post, $"/api/v1/study-sessions/{sessionId}/answers", body, childToken);
                s.Should().Be(200, b.ToString());
            }
            await _client.SendAsync(HttpMethod.Post, $"/api/v1/study-sessions/{sessionId}/navigate", new { direction = "next" }, childToken);
        }
        (s, b) = await _client.SendAsync(HttpMethod.Post, $"/api/v1/study-sessions/{sessionId}/pause", token: childToken);
        s.Should().Be(200); b.Data().Str("status").Should().Be("Paused");
        (s, b) = await _client.SendAsync(HttpMethod.Post, $"/api/v1/study-sessions/{sessionId}/resume", token: childToken);
        s.Should().Be(200); b.Data().Str("status").Should().Be("Active");
        (s, b) = await _client.SendAsync(HttpMethod.Post, $"/api/v1/study-sessions/{sessionId}/complete", token: childToken);
        s.Should().Be(200, b.ToString());
        b.Data().GetProperty("scorePercent").GetDecimal().Should().BeGreaterThanOrEqualTo(0);
        b.Data().GetProperty("attemptNumber").GetInt32().Should().Be(1);

        // Progress recorded
        (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/students/me/progress", token: childToken);
        s.Should().Be(200); b.Data().GetProperty("sessionsCompleted").GetInt32().Should().Be(1);

        // Parent can see child's progress; child cannot use parent endpoints; parent cannot start sessions
        (s, b) = await _client.SendAsync(HttpMethod.Get, $"/api/v1/parents/me/children/{studentId}/progress", token: parentToken);
        s.Should().Be(200); b.Data().GetProperty("sessionsCompleted").GetInt32().Should().Be(1);
        (s, _) = await _client.SendAsync(HttpMethod.Get, "/api/v1/parents/me/children", token: childToken);
        s.Should().Be(403);
        (s, _) = await _client.SendAsync(HttpMethod.Post, "/api/v1/study-sessions", new { lessonId }, parentToken);
        s.Should().Be(403);
        (s, _) = await _client.SendAsync(HttpMethod.Get, "/api/v1/admin/dashboard", token: parentToken);
        s.Should().Be(403);
    }

    [Fact]
    public async Task Parent_cannot_access_another_parents_child()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        async Task<(string token, string? studentId)> MakeParent(string tag, bool withChild)
        {
            var email = $"it-{tag}-{stamp}@tutor365.test";
            await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email, password = "Parent1234", firstName = tag, lastName = "P" });
            var code = await GetOtpAsync(email, 1);
            var (_, b) = await _client.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { email, code });
            var token = b.Data().Str("accessToken");
            if (!withChild) return (token, null);
            var (_, years) = await _client.SendAsync(HttpMethod.Get, "/api/v1/year-groups");
            var year = years.Data()[0].Str("id");
            var (_, c) = await _client.SendAsync(HttpMethod.Post, "/api/v1/parents/me/children", new { firstName = "C", lastName = tag, email = $"it-{tag}-child-{stamp}@tutor365.test", password = "Child1234", yearGroupId = year }, token);
            return (token, c.Data().GetProperty("summary").Str("studentId"));
        }
        var (_, childOfA) = await MakeParent("a", true);
        var (tokenB, _) = await MakeParent("b", false);
        var (s, _) = await _client.SendAsync(HttpMethod.Get, $"/api/v1/parents/me/children/{childOfA}", token: tokenB);
        s.Should().Be(403);
        (s, _) = await _client.SendAsync(HttpMethod.Get, $"/api/v1/parents/me/children/{childOfA}/progress", token: tokenB);
        s.Should().Be(403);
    }

    [Fact]
    public async Task Unauthenticated_requests_get_envelope_401()
    {
        var (s, b) = await _client.SendAsync(HttpMethod.Get, "/api/v1/students/me/dashboard");
        s.Should().Be(401);
        b.GetProperty("success").GetBoolean().Should().BeFalse();
        b.Str("errorCode").Should().Be("UNAUTHORIZED");
    }
}
