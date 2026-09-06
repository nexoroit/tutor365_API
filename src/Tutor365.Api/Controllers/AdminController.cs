using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;
using Tutor365.Domain.Enums;
using Tutor365.Infrastructure.Data.Seed;

namespace Tutor365.Api.Controllers;

/// <summary>Administration: dashboard, users, curriculum/content management, settings, audit logs. Admin role only.</summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : ApiControllerBase
{
    private readonly IAdminService _admin;
    private readonly ICurriculumService _curriculum;
    public AdminController(IAdminService admin, ICurriculumService curriculum) { _admin = admin; _curriculum = curriculum; }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardDto>), 200)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await _admin.GetDashboardAsync(ct));

    // ---- users ----
    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminUserDto>>), 200)]
    public async Task<IActionResult> Users([FromQuery] AdminUserQuery query, CancellationToken ct) => Ok(await _admin.GetUsersAsync(query, ct));

    [HttpGet("users/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), 200)]
    public async Task<IActionResult> User(Guid id, CancellationToken ct) => Ok(await _admin.GetUserAsync(id, ct));

    /// <summary>Create an Admin or Parent account (students are created by their parent).</summary>
    [HttpPost("users")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), 201)]
    public async Task<IActionResult> CreateUser([FromBody] CreateAdminUserRequest request, CancellationToken ct)
    {
        var u = await _admin.CreateUserAsync(request, ct);
        return Created($"/api/v1/admin/users/{u.Id}", u, "User created.");
    }

    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), 200)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] AdminUpdateUserRequest request, CancellationToken ct) => Ok(await _admin.UpdateUserAsync(id, request, ct));

    [HttpPost("users/{id:guid}/password")]
    public async Task<IActionResult> SetPassword(Guid id, [FromBody] AdminSetPasswordRequest request, CancellationToken ct) { await _admin.SetPasswordAsync(id, request, ct); return OkMessage("Password set."); }

    /// <summary>Soft delete: anonymises and disables the account; learning history is retained.</summary>
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct) { await _admin.DeleteUserAsync(id, ct); return OkMessage("User deleted."); }

    // ---- audit & settings ----
    [HttpGet("audit-logs")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), 200)]
    public async Task<IActionResult> AuditLogs([FromQuery] AuditLogQuery query, CancellationToken ct) => Ok(await _admin.GetAuditLogsAsync(query, ct));

    [HttpGet("system-settings")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SystemSettingDto>>), 200)]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await _admin.GetSettingsAsync(ct));

    [HttpPut("system-settings/{key}")]
    [ProducesResponseType(typeof(ApiResponse<SystemSettingDto>), 200)]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSystemSettingRequest request, CancellationToken ct) => Ok(await _admin.UpdateSettingAsync(key, request, ct));

    // ---- mail settings (stored in SystemSettings, password encrypted) ----
    [HttpGet("mail-settings")]
    [ProducesResponseType(typeof(ApiResponse<MailSettingsDto>), 200)]
    public async Task<IActionResult> MailSettings([FromServices] IMailSettingsService mail, CancellationToken ct) => Ok(await mail.GetAsync(ct));

    /// <summary>Update SMTP settings. Leave password empty to keep the existing one.</summary>
    [HttpPut("mail-settings")]
    [ProducesResponseType(typeof(ApiResponse<MailSettingsDto>), 200)]
    public async Task<IActionResult> UpdateMailSettings([FromServices] IMailSettingsService mail, [FromBody] UpdateMailSettingsRequest request, CancellationToken ct) => Ok(await mail.UpdateAsync(request, ct));

    /// <summary>Send a test email with the stored settings.</summary>
    [HttpPost("mail-settings/test")]
    public async Task<IActionResult> TestMail([FromServices] IAppEmailService emails, [FromBody] SendTestMailRequest request, CancellationToken ct)
    {
        try
        {
            await emails.SendTestAsync(request.ToEmail, ct);
            return OkMessage($"Test email sent to {request.ToEmail}.");
        }
        catch (Domain.Exceptions.BusinessRuleException ex) when (ex.Data.Contains("detail"))
        {
            // Admin-only diagnostics: surface the SMTP failure reason so it can be fixed without server log access.
            return UnprocessableEntity(ApiResponse.Fail(ex.ErrorCode, $"SMTP failed: {ex.Data["detail"]}"));
        }
    }

    /// <summary>Rendered HTML of a branded email for preview: kind = otp | welcome | child | weekly | notification.</summary>
    [HttpGet("mail-settings/preview")]
    [Produces("text/html")]
    public IActionResult PreviewMail([FromServices] IAppEmailService emails, [FromQuery] string kind = "otp") => Content(emails.Preview(kind).Html, "text/html");

    /// <summary>Send a sample branded email of the given kind to an address (for checking templates in real mail clients).</summary>
    [HttpPost("mail-settings/send-sample")]
    public async Task<IActionResult> SendSample([FromServices] IAppEmailService emails, [FromQuery] string kind, [FromBody] SendTestMailRequest request, CancellationToken ct)
    {
        await emails.SendSampleAsync(kind, request.ToEmail, ct);
        return OkMessage($"Sample '{kind}' sent to {request.ToEmail}.");
    }

    // ---- AI settings (stored in SystemSettings, key encrypted and only shown as a hint) ----
    [HttpGet("ai-settings")]
    [ProducesResponseType(typeof(ApiResponse<AiSettingsDto>), 200)]
    public async Task<IActionResult> AiSettings([FromServices] IAiSettingsService ai, CancellationToken ct) => Ok(await ai.GetAsync(ct));

    /// <summary>Update AI settings. Leave apiKey empty to keep the existing key.</summary>
    [HttpPut("ai-settings")]
    [ProducesResponseType(typeof(ApiResponse<AiSettingsDto>), 200)]
    public async Task<IActionResult> UpdateAiSettings([FromServices] IAiSettingsService ai, [FromBody] UpdateAiSettingsRequest request, CancellationToken ct) => Ok(await ai.UpdateAsync(request, ct));

    /// <summary>Round-trip a short prompt through the configured provider.</summary>
    [HttpPost("ai-settings/test")]
    [ProducesResponseType(typeof(ApiResponse<AiTestResponse>), 200)]
    public async Task<IActionResult> TestAi([FromServices] Application.Interfaces.IAiProvider provider, [FromServices] IAiSettingsService ai, CancellationToken ct)
    {
        var cfg = await ai.GetConfigAsync(ct);
        var r = await provider.CompleteAsync(new Application.Interfaces.AiRequest("You are tutor365's GCSE tutor. Reply in one short sentence.", new[] { new Application.Interfaces.AiChatMessage("user", "Say hello to a Year 10 student starting a chemistry lesson.") }, 120, "tutor"), ct);
        return Ok(new AiTestResponse(!r.IsStub, provider.ProviderName, r.Model ?? cfg.TutorModel, r.Reply(), r.InputTokens, r.OutputTokens));
    }

    // ---- curriculum ----
    /// <summary>Full curriculum for a subject including draft/archived items (admin view).</summary>
    [HttpGet("curriculum")]
    [ProducesResponseType(typeof(ApiResponse<CurriculumTreeDto>), 200)]
    public async Task<IActionResult> Curriculum([FromQuery] Guid subjectId, [FromQuery] Guid? examBoardId, CancellationToken ct) => Ok(await _curriculum.GetTreeAsync(subjectId, examBoardId, true, ct));

    [HttpPost("topics")]
    [ProducesResponseType(typeof(ApiResponse<TopicDto>), 200)]
    public async Task<IActionResult> CreateTopic([FromBody] UpsertTopicRequest request, CancellationToken ct) => Ok(await _admin.UpsertTopicAsync(null, request, ct));
    [HttpPut("topics/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TopicDto>), 200)]
    public async Task<IActionResult> UpdateTopic(Guid id, [FromBody] UpsertTopicRequest request, CancellationToken ct) => Ok(await _admin.UpsertTopicAsync(id, request, ct));

    [HttpPost("subtopics")]
    [ProducesResponseType(typeof(ApiResponse<SubTopicDto>), 200)]
    public async Task<IActionResult> CreateSubTopic([FromBody] UpsertSubTopicRequest request, CancellationToken ct) => Ok(await _admin.UpsertSubTopicAsync(null, request, ct));
    [HttpPut("subtopics/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SubTopicDto>), 200)]
    public async Task<IActionResult> UpdateSubTopic(Guid id, [FromBody] UpsertSubTopicRequest request, CancellationToken ct) => Ok(await _admin.UpsertSubTopicAsync(id, request, ct));

    [HttpGet("lessons")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LessonSummaryDto>>), 200)]
    public async Task<IActionResult> Lessons([FromQuery] Guid? subTopicId, [FromQuery] Guid? topicId, [FromQuery] Guid? subjectId, CancellationToken ct) => Ok(await _curriculum.GetLessonsAsync(subTopicId, topicId, subjectId, false, ct));
    [HttpGet("lessons/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailDto>), 200)]
    public async Task<IActionResult> Lesson(Guid id, CancellationToken ct) => Ok(await _curriculum.GetLessonAsync(id, false, ct));
    [HttpPost("lessons")]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailDto>), 200)]
    public async Task<IActionResult> CreateLesson([FromBody] UpsertLessonRequest request, CancellationToken ct) => Ok(await _admin.UpsertLessonAsync(null, request, ct));
    [HttpPut("lessons/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailDto>), 200)]
    public async Task<IActionResult> UpdateLesson(Guid id, [FromBody] UpsertLessonRequest request, CancellationToken ct) => Ok(await _admin.UpsertLessonAsync(id, request, ct));
    /// <summary>Ask the AI to write new variant questions for every question step of a lesson (count per step). Returns how many were created.</summary>
    [HttpPost("lessons/{id:guid}/generate-variants")]
    [ProducesResponseType(typeof(ApiResponse<int>), 200)]
    public async Task<IActionResult> GenerateVariants([FromServices] Application.Interfaces.IQuestionGenerator generator, Guid id, [FromQuery] int count = 1, CancellationToken ct = default)
    {
        var made = await generator.GenerateVariantsAsync(id, null, Math.Clamp(count, 1, 3), ct);
        return Ok(made, made == 0 ? "No variants were generated. Check that the AI provider is enabled and Questions.GenerateVariants is true." : $"{made} new variant question{(made == 1 ? "" : "s")} added.");
    }

    /// <summary>Replace the ordered activity list of a lesson.</summary>
    [HttpPut("lessons/{id:guid}/activities")]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailDto>), 200)]
    public async Task<IActionResult> ReplaceActivities(Guid id, [FromBody] List<UpsertLessonActivityRequest> activities, CancellationToken ct) => Ok(await _admin.ReplaceLessonActivitiesAsync(id, activities, ct));

    [HttpGet("questions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminQuestionDto>>), 200)]
    public async Task<IActionResult> Questions([FromQuery] AdminQuestionQuery query, CancellationToken ct) => Ok(await _admin.GetQuestionsAsync(query, ct));
    [HttpGet("questions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminQuestionDto>), 200)]
    public async Task<IActionResult> Question(Guid id, CancellationToken ct) => Ok(await _admin.GetQuestionAsync(id, ct));
    [HttpPost("questions")]
    [ProducesResponseType(typeof(ApiResponse<AdminQuestionDto>), 200)]
    public async Task<IActionResult> CreateQuestion([FromBody] UpsertQuestionRequest request, CancellationToken ct) => Ok(await _admin.UpsertQuestionAsync(null, request, ct));
    [HttpPut("questions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminQuestionDto>), 200)]
    public async Task<IActionResult> UpdateQuestion(Guid id, [FromBody] UpsertQuestionRequest request, CancellationToken ct) => Ok(await _admin.UpsertQuestionAsync(id, request, ct));

    /// <summary>Publish / draft / archive a topic, subtopic, lesson or question.</summary>
    [HttpPost("{entity}/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(string entity, Guid id, [FromQuery] ContentStatus status, CancellationToken ct) { await _admin.SetStatusAsync(entity, id, status, ct); return OkMessage($"Status set to {status}."); }

    // ---- logs ----
    /// <summary>Dates that have a log file, newest first.</summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LogFileDto>>), 200)]
    public IActionResult LogFiles([FromServices] IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "Logs");
        if (!Directory.Exists(dir)) return Ok(Array.Empty<LogFileDto>());
        var files = new DirectoryInfo(dir).GetFiles("tutor365-*.log").OrderByDescending(f => f.Name)
            .Select(f => new LogFileDto(f.Name.Substring(9, 8) is var d && d.Length == 8 ? $"{d[..4]}-{d[4..6]}-{d[6..]}" : f.Name, f.Length, f.LastWriteTimeUtc)).ToList();
        return Ok(files);
    }

    /// <summary>Tail of one day's application log (newest last). Lines are capped at 2000.</summary>
    [HttpGet("logs/{date}")]
    [ProducesResponseType(typeof(ApiResponse<LogTailDto>), 200)]
    public async Task<IActionResult> LogTail([FromServices] IWebHostEnvironment env, string date, [FromQuery] int lines = 500, [FromQuery] string? level = null, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)) throw new Tutor365.Domain.Exceptions.AppValidationException("date", "Use yyyy-MM-dd.");
        var path = Path.Combine(env.ContentRootPath, "Logs", $"tutor365-{day:yyyyMMdd}.log");
        if (!System.IO.File.Exists(path)) return Ok(new LogTailDto(date, 0, Array.Empty<string>()));
        lines = Math.Clamp(lines, 50, 2000);
        var all = new List<string>();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs))
        {
            // Group continuation lines (stack traces, email bodies) with the entry that started them.
            string? current = null;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith('[') && line.Length > 14 && line[3] == ':') { if (current != null) all.Add(current); current = line; }
                else current = current == null ? line : current + "\n" + line;
            }
            if (current != null) all.Add(current);
        }
        IEnumerable<string> q = all;
        if (!string.IsNullOrWhiteSpace(level)) { var tag = $" {level.Trim().ToUpperInvariant()[..3]}] "; q = q.Where(l => l.Contains(tag)); }
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(l => l.Contains(search, StringComparison.OrdinalIgnoreCase));
        var filtered = q.ToList();
        return Ok(new LogTailDto(date, filtered.Count, filtered.TakeLast(lines).ToList()));
    }

    /// <summary>Re-import lesson content JSON from the configured content directory (idempotent).</summary>
    [HttpPost("content/import")]
    [ProducesResponseType(typeof(ApiResponse<ContentImportResultDto>), 200)]
    public async Task<IActionResult> ImportContent([FromServices] ContentSeeder seeder, [FromServices] IOptions<AppOptions> options, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        var candidates = new[] { Path.IsPathRooted(options.Value.ContentPath) ? options.Value.ContentPath : Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.ContentPath)), Path.Combine(env.ContentRootPath, "content", "lessons"), Path.Combine(AppContext.BaseDirectory, "content", "lessons") };
        var path = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        var r = await seeder.ImportDirectoryAsync(path, ct);
        return Ok(new ContentImportResultDto(r.Files, r.Lessons, r.Questions, r.Errors));
    }
}
