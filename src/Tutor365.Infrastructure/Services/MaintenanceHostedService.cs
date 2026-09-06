using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tutor365.Application.Services;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Infrastructure.Data;

namespace Tutor365.Infrastructure.Services;

/// <summary>Hourly housekeeping: abandon stale sessions, mark missed slots, flag overdue plan items, send weekly report notifications, prune expired tokens/OTPs.</summary>
public class MaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MaintenanceHostedService> _logger;
    public MaintenanceHostedService(IServiceScopeFactory scopes, ILogger<MaintenanceHostedService> logger) { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Maintenance run failed"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Runs can be triggered by the timer and by tests/admin at the same time; never let two overlap.
        await Gate.WaitAsync(ct);
        try { await RunOnceCoreAsync(ct); }
        finally { Gate.Release(); }
    }

    private async Task RunOnceCoreAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow; var today = DateOnly.FromDateTime(now);

        var abandonHours = int.TryParse(await db.SystemSettings.Where(s => s.Key == "Session.AbandonAfterHours").Select(s => s.Value).FirstOrDefaultAsync(ct), out var h) ? h : 48;
        var stale = await db.StudySessions.Where(s => (s.Status == StudySessionStatus.Active || s.Status == StudySessionStatus.Paused) && s.LastActivityAt < now.AddHours(-abandonHours)).ToListAsync(ct);
        foreach (var s in stale) s.Status = StudySessionStatus.Abandoned;

        var missed = await db.DailyStudySlots.Where(s => s.Date < today && (s.Status == DailySlotStatus.Scheduled || s.Status == DailySlotStatus.InProgress)).ToListAsync(ct);
        foreach (var s in missed) s.Status = DailySlotStatus.Missed;

        var overdue = await db.StudyPlanItems.Where(i => i.DueDate < today && (i.Status == StudyPlanItemStatus.Pending || i.Status == StudyPlanItemStatus.InProgress)).ToListAsync(ct);
        foreach (var i in overdue) i.Status = StudyPlanItemStatus.Overdue;

        var expiredTokens = await db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-7)).ToListAsync(ct);
        db.RefreshTokens.RemoveRange(expiredTokens);
        var oldOtps = await db.OtpCodes.Where(o => o.ExpiresAt < now.AddDays(-1)).ToListAsync(ct);
        db.OtpCodes.RemoveRange(oldOtps);

        // Weekly report notification for parents (once per week, on the configured day).
        var reportDay = await db.SystemSettings.Where(s => s.Key == "Reports.WeeklyReportDay").Select(s => s.Value).FirstOrDefaultAsync(ct) ?? "Sunday";
        if (Enum.TryParse<DayOfWeek>(reportDay, true, out var dow) && now.DayOfWeek == dow && now.Hour >= 18)
        {
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var links = await db.StudentParents.Include(sp => sp.Parent).ThenInclude(p => p.User).Include(sp => sp.Student).ThenInclude(s => s.User)
                .Where(sp => sp.Parent.WeeklyReportEnabled).ToListAsync(ct);
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var emails = scope.ServiceProvider.GetRequiredService<IAppEmailService>();
            foreach (var link in links)
            {
                var already = await db.Notifications.AnyAsync(n => n.UserId == link.Parent.UserId && n.Type == NotificationType.WeeklyReport && n.CreatedAt >= weekStart.ToDateTime(TimeOnly.MinValue) && n.DataJson!.Contains(link.StudentId.ToString()), ct);
                if (already) continue;
                var report = await reports.BuildWeeklyReportAsync(link.StudentId, weekStart, ct);
                db.Notifications.Add(new Notification
                {
                    UserId = link.Parent.UserId, Type = NotificationType.WeeklyReport, Title = $"Weekly report for {link.Student.User.FirstName}",
                    Message = report.Summary,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new { studentId = link.StudentId, weekStart })
                });
                if (link.Parent.EmailNotifications && link.Parent.User.IsActive) await emails.SendWeeklyReportAsync(link.Parent.User, report, ct);
            }
        }

        var changed = await db.SaveChangesAsync(ct);

        // Alerts for what was just marked missed/overdue. Statuses only transition once, so each alert is sent once.
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        foreach (var group in missed.GroupBy(s => new { s.StudentId, s.Date }))
        {
            var doneThatDay = await db.DailyStudySlots.CountAsync(s => s.StudentId == group.Key.StudentId && s.Date == group.Key.Date && (s.Status == DailySlotStatus.Completed || s.Status == DailySlotStatus.Skipped), ct);
            if (doneThatDay > 0) continue; // partial days are visible on the dashboard; only a fully missed day warrants an alert
            var first = await db.Students.Where(x => x.Id == group.Key.StudentId).Select(x => x.User.FirstName).FirstOrDefaultAsync(ct);
            if (first == null) continue;
            var marker = $"\"missedDate\":\"{group.Key.Date:yyyy-MM-dd}\"";
            var alreadySent = await db.Notifications.AnyAsync(n => n.Type == NotificationType.PerformanceAlert && n.DataJson != null && n.DataJson.Contains(marker) && n.DataJson.Contains(group.Key.StudentId.ToString()), ct);
            if (alreadySent) continue;
            var n = group.Count();
            var when = group.Key.Date == today.AddDays(-1) ? "yesterday" : $"on {group.Key.Date:ddd d MMM}";
            await notifications.NotifyParentsOfStudentAsync(group.Key.StudentId, NotificationType.PerformanceAlert,
                $"{first} missed {when}'s study sessions",
                $"{first} didn't start any of the {n} study session{(n == 1 ? "" : "s")} planned {when}. A quick word of encouragement usually helps; the timetable will catch up automatically.",
                new { studentId = group.Key.StudentId, missedDate = group.Key.Date, slots = n }, ct);
        }
        foreach (var item in overdue)
        {
            var info = await db.StudyPlanItems.Where(i => i.Id == item.Id)
                .Select(i => new { StudentId = i.StudyPlan.StudentId, First = i.StudyPlan.Student.User.FirstName, StudentUserId = i.StudyPlan.Student.UserId, PlanTitle = i.StudyPlan.Title,
                    Subject = i.Subject.Name, Lesson = i.Lesson != null ? i.Lesson.Title : null, Topic = db.Topics.Where(t => t.Id == i.TopicId).Select(t => t.Name).FirstOrDefault(), i.ItemType })
                .FirstOrDefaultAsync(ct);
            if (info == null || item.DueDate == null) continue;
            var what = info.Lesson ?? info.Topic ?? info.Subject;
            var kind = info.ItemType switch { StudyPlanItemType.TopicTest => "topic test", StudyPlanItemType.MockExam => "mock exam", StudyPlanItemType.TopicReview => "review", _ => "lesson" };
            await notifications.NotifyAsync(info.StudentUserId, NotificationType.General, "Assigned work is overdue",
                $"The {kind} \"{what}\" from \"{info.PlanTitle}\" was due {item.DueDate:ddd d MMM}. Open your plan to catch up.", new { planItemId = item.Id }, ct);
            await notifications.NotifyParentsOfStudentAsync(info.StudentId, NotificationType.PerformanceAlert,
                $"{info.First} has overdue assigned work",
                $"The {kind} \"{what}\" from \"{info.PlanTitle}\" was due {item.DueDate:ddd d MMM} and hasn't been completed yet.",
                new { studentId = info.StudentId, planItemId = item.Id }, ct);
        }

        _logger.LogInformation("Maintenance: {Stale} sessions abandoned, {Missed} slots missed, {Overdue} items overdue, {Tokens} tokens pruned ({Changed} rows)", stale.Count, missed.Count, overdue.Count, expiredTokens.Count, changed);
    }
}
