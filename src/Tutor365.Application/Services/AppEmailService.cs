using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using static Tutor365.Application.Common.EmailTemplates;

namespace Tutor365.Application.Services;

/// <summary>All branded transactional emails. Sending never throws into the calling flow; failures are logged.</summary>
public interface IAppEmailService
{
    Task SendParentWelcomeAsync(User parent, CancellationToken ct = default);
    Task SendChildCreatedAsync(User parent, User child, CancellationToken ct = default);
    Task SendWeeklyReportAsync(User parent, WeeklyReportDto report, CancellationToken ct = default);
    Task SendNotificationAsync(User recipient, NotificationType type, string title, string message, string? childName, CancellationToken ct = default);
    Task SendTestAsync(string toEmail, CancellationToken ct = default);
    /// <summary>Rendered HTML for previews (admin).</summary>
    (string Html, string Text) Preview(string kind);
}

public class AppEmailService : IAppEmailService
{
    private readonly IEmailSender _sender;
    private readonly AppOptions _app;
    private readonly ILogger<AppEmailService> _logger;

    public AppEmailService(IEmailSender sender, IOptions<AppOptions> app, ILogger<AppEmailService> logger) { _sender = sender; _app = app.Value; _logger = logger; }

    private (string Html, string Text) Render(string title, string preheader, string bodyHtml, string bodyText, (string, string)? cta = null)
        => Layout(_app.Name, _app.FrontendUrl, _app.SupportEmail, title, preheader, bodyHtml, bodyText, cta);

    private async Task SafeSendAsync(string email, string name, string subject, (string Html, string Text) content, CancellationToken ct)
    {
        try { await _sender.SendAsync(new EmailMessage(email, name, subject, content.Html, content.Text), ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Email '{Subject}' to {Email} was not sent", subject, email); }
    }

    public Task SendParentWelcomeAsync(User parent, CancellationToken ct = default)
    {
        var c = Render("Welcome to tutor365", "Your account is ready. Add your child to get started.",
            Para($"Hi {E(parent.FirstName)},") +
            Para($"Your email is verified and your {E(_app.Name)} account is ready. Here's how it works:") +
            List(new[] { "<strong>Add your child</strong> with their own email and password.", "Set their <strong>study schedule</strong> (for example 2 × 45 minutes a day) and target grades.", $"{E(_app.Name)} builds their weekly timetable, teaches each lesson, marks the questions and unlocks the next lesson once they pass.", "You get progress, test results and a weekly report." }) +
            Para("Log in to add your first child."),
            $"Hi {parent.FirstName},\n\nYour account is ready. Log in to add your child, set their schedule and target grades, and {_app.Name} will build their timetable.",
            ("Add your child", $"{_app.FrontendUrl}/parent/children"));
        return SafeSendAsync(parent.Email, parent.FullName, $"Welcome to {_app.Name}", c, ct);
    }

    public async Task SendChildCreatedAsync(User parent, User child, CancellationToken ct = default)
    {
        var toParent = Render($"{E(child.FirstName)}'s account is ready", $"{child.FirstName} can now log in to {_app.Name}.",
            Para($"Hi {E(parent.FirstName)},") +
            Para($"You've created a student account for <strong>{E(child.FullName)}</strong>. They can sign in with <strong>{E(child.Email)}</strong> and the password you chose.") +
            Para("Their first week's timetable is ready. You can change how many sessions they do each day, their target grades and the pass mark from the Children page.") +
            Para("<span style=\"color:#6B7280\">Tip: keep an eye on the weekly report every Sunday.</span>"),
            $"Hi {parent.FirstName},\n\nYou've created a student account for {child.FullName}. They can sign in with {child.Email} and the password you chose.",
            ("View timetable & settings", $"{_app.FrontendUrl}/parent/children"));
        await SafeSendAsync(parent.Email, parent.FullName, $"{child.FirstName}'s {_app.Name} account is ready", toParent, ct);

        var toChild = Render($"Hi {E(child.FirstName)}, welcome to tutor365", "Your parent has set up your account. Log in to see today's study plan.",
            Para($"Your parent has set up your {E(_app.Name)} account. Sign in with <strong>{E(child.Email)}</strong> and the password they gave you.") +
            Para("Every day you'll get a short plan: a lesson to read, a few questions to try, and instant feedback. Pass the questions to unlock the next lesson. Stuck? Use the hint or ask the tutor.") +
            Para("If you forget your password, use <em>Forgot password</em> on the login page and we'll email you a code."),
            $"Hi {child.FirstName},\n\nYour parent has set up your {_app.Name} account. Sign in with {child.Email} and the password they gave you.",
            ("Start studying", $"{_app.FrontendUrl}/login"));
        await SafeSendAsync(child.Email, child.FullName, $"Welcome to {_app.Name}, {child.FirstName}", toChild, ct);
    }

    public Task SendWeeklyReportAsync(User parent, WeeklyReportDto r, CancellationToken ct = default)
    {
        var first = r.StudentName.Split(' ')[0];
        var stats = "<table role=\"presentation\" width=\"100%\" cellspacing=\"6\" cellpadding=\"0\" border=\"0\"><tr>" +
            Stat("Study time", $"{r.StudyMinutes / 60}h {r.StudyMinutes % 60}m") + Stat("Sessions", r.SessionsPlanned > 0 ? $"{r.Sessions}/{r.SessionsPlanned}" : r.Sessions.ToString()) +
            Stat("Average", r.AverageScore.HasValue ? $"{Math.Round(r.AverageScore.Value)}%" : "–") + Stat("Streak", $"{r.CurrentStreakDays}d") + "</tr></table>";
        var subjects = "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"6\" border=\"0\" style=\"font-size:14px;border-collapse:collapse\">" +
            "<tr style=\"color:#6B7280;font-size:12px;text-transform:uppercase\"><td>Subject</td><td align=\"right\">This week</td><td align=\"right\">Last week</td><td align=\"right\">Grade</td></tr>" +
            string.Join("", r.Subjects.Select(s => $"<tr style=\"border-top:1px solid #E5E7EB\"><td>{E(s.SubjectName)}</td><td align=\"right\">{(s.AverageScore.HasValue ? Math.Round(s.AverageScore.Value) + "%" : "–")}</td><td align=\"right\">{(s.PreviousWeekScore.HasValue ? Math.Round(s.PreviousWeekScore.Value) + "%" : "–")}</td><td align=\"right\">{(s.CurrentGrade?.ToString() ?? "–")} / {s.TargetGrade}</td></tr>")) + "</table>";
        var body = Para($"Hi {E(parent.FirstName)},") + Para(E(r.Summary)) + stats +
            (r.Improved.Count > 0 ? Para("<strong>Improving</strong>") + List(r.Improved.Select(i => $"{E(i.SubjectName)}: {E(i.Note)}")) : "") +
            (r.NeedsAttention.Count > 0 ? Para("<strong>Needs attention</strong>") + List(r.NeedsAttention.Select(i => $"{E(i.SubjectName)}: {E(i.Note)}")) : "") +
            Para("<strong>Subjects</strong>") + subjects +
            (r.RecommendedFocus.Count > 0 ? Para("<strong>Recommended focus next week</strong>") + List(r.RecommendedFocus.Select(f => $"{E(f.SubjectName)} · {E(f.LessonTitle ?? f.TopicName)}: {E(f.Reason)}")) : "");
        var text = $"Hi {parent.FirstName},\n\n{r.Summary}\n\nStudy time {r.StudyMinutes / 60}h {r.StudyMinutes % 60}m · {r.Sessions} sessions · average {(r.AverageScore.HasValue ? Math.Round(r.AverageScore.Value) + "%" : "–")}\n" +
            string.Join("\n", r.Subjects.Select(s => $"- {s.SubjectName}: {(s.AverageScore.HasValue ? Math.Round(s.AverageScore.Value) + "%" : "–")}, grade {s.CurrentGrade?.ToString() ?? "–"}/{s.TargetGrade}"));
        var c = Render($"{E(first)}'s week: {r.WeekStart:d MMM} – {r.WeekEnd:d MMM}", r.Summary, body, text, ("Open the full report", $"{_app.FrontendUrl}/parent/reports"));
        return SafeSendAsync(parent.Email, parent.FullName, $"{first}'s weekly report · {r.WeekStart:d MMM}", c, ct);
    }

    public Task SendNotificationAsync(User recipient, NotificationType type, string title, string message, string? childName, CancellationToken ct = default)
    {
        var link = recipient.Role == UserRole.Student ? $"{_app.FrontendUrl}/student/dashboard" : $"{_app.FrontendUrl}/parent/dashboard";
        var c = Render(title, message, Para($"Hi {E(recipient.FirstName)},") + Para(E(message)), $"Hi {recipient.FirstName},\n\n{message}", ("Open tutor365", link));
        return SafeSendAsync(recipient.Email, recipient.FullName, $"{_app.Name}: {title}", c, ct);
    }

    public Task SendTestAsync(string toEmail, CancellationToken ct = default)
    {
        var c = Render("Email is working", "Your SMTP settings are configured correctly.",
            Para("This is a test message from <strong>tutor365</strong>.") + Para("If you can read this, outgoing email is configured correctly and students and parents will receive verification codes, welcome emails and weekly reports.") + CodeBox("123456") + Para("<span style=\"color:#6B7280\">Example of how a verification code appears.</span>"),
            "This is a test message from tutor365. Outgoing email is configured correctly.", ("Open tutor365", _app.FrontendUrl));
        return _sender.SendAsync(new EmailMessage(toEmail, toEmail, $"{_app.Name} test email", c.Html, c.Text), ct);
    }

    public (string Html, string Text) Preview(string kind)
    {
        var parent = new User { FirstName = "Priya", LastName = "Tester", Email = "parent@example.com", Role = UserRole.Parent };
        var child = new User { FirstName = "Sam", LastName = "Tester", Email = "sam@example.com", Role = UserRole.Student };
        switch (kind.ToLowerInvariant())
        {
            case "otp":
                return Render("Verify your email address", "Your code is 482913", Para("Hi Priya,") + Para("Thanks for registering. Enter this code in the app to verify your email and finish setting up your account.") + CodeBox("482913") + Para("<span style=\"color:#6B7280\">This code expires in 10 minutes.</span>"), "Your code: 482913");
            case "welcome":
                return Render("Welcome to tutor365", "", Para("Hi Priya,") + Para("Your email is verified and your account is ready."), "", ("Add your child", $"{_app.FrontendUrl}/parent/children"));
            case "child":
                return Render($"Hi Sam, welcome to tutor365", "", Para("Your parent has set up your account. Sign in with <strong>sam@example.com</strong>.") + Para("Every day you'll get a short plan: a lesson, a few questions and instant feedback."), "", ("Start studying", $"{_app.FrontendUrl}/login"));
            case "weekly":
                var report = new WeeklyReportDto(Guid.Empty, child.FullName, "Year 10", new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 6), 255, 6, 10, 72, 84, 61, 3, 4,
                    new[] { new SubjectWeekDto(Guid.Empty, "Mathematics", null, 90, 2, 78, 70, 7, 6), new SubjectWeekDto(Guid.Empty, "Chemistry", null, 90, 2, 55, 62, 7, 5), new SubjectWeekDto(Guid.Empty, "Biology", null, 75, 2, 81, null, 7, 8) },
                    new[] { new SubjectChangeDto(Guid.Empty, "Mathematics", 70, 78, "Up 8 points on last week.") },
                    new[] { new SubjectChangeDto(Guid.Empty, "Chemistry", 62, 55, "Averaging 55%, below the 70% needed for grade 7.") },
                    Array.Empty<TopicProgressDto>(), new[] { new RecommendationDto(Guid.Empty, "Chemistry", null, null, "Quantitative chemistry", null, "The mole and relative formula mass", "Lesson", "Weakest topic this week.", 1, 45) },
                    Array.Empty<WeeklyPointDto>(), "Sam studied for 4h 15m across 6 sessions, averaging 72% and passed 3 lessons. Improving in Mathematics. Needs attention: Chemistry.");
                var first = "Sam";
                var body = Para("Hi Priya,") + Para(E(report.Summary)) + "<table role=\"presentation\" width=\"100%\" cellspacing=\"6\" cellpadding=\"0\" border=\"0\"><tr>" + Stat("Study time", "4h 15m") + Stat("Sessions", "6/10") + Stat("Average", "72%") + Stat("Streak", "4d") + "</tr></table>" + Para("<strong>Improving</strong>") + List(new[] { "Mathematics: Up 8 points on last week." }) + Para("<strong>Needs attention</strong>") + List(new[] { "Chemistry: Averaging 55%, below the 70% needed for grade 7." });
                return Render($"{first}'s week: 31 Aug – 6 Sept", report.Summary, body, report.Summary, ("Open the full report", $"{_app.FrontendUrl}/parent/reports"));
            default:
                return Render("Test passed!", "", Para("Hi Sam,") + Para("Energy changes topic test: 82%, estimated grade 8. Great work."), "", ("Open tutor365", _app.FrontendUrl));
        }
    }
}
