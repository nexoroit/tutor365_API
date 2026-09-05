using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<PagedResult<AdminUserDto>> GetUsersAsync(AdminUserQuery query, CancellationToken ct = default);
    Task<AdminUserDto> GetUserAsync(Guid id, CancellationToken ct = default);
    Task<AdminUserDto> CreateUserAsync(CreateAdminUserRequest request, CancellationToken ct = default);
    Task<AdminUserDto> UpdateUserAsync(Guid id, AdminUpdateUserRequest request, CancellationToken ct = default);
    Task SetPasswordAsync(Guid id, AdminSetPasswordRequest request, CancellationToken ct = default);
    Task DeleteUserAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<SystemSettingDto>> GetSettingsAsync(CancellationToken ct = default);
    Task<SystemSettingDto> UpdateSettingAsync(string key, UpdateSystemSettingRequest request, CancellationToken ct = default);

    Task<TopicDto> UpsertTopicAsync(Guid? id, UpsertTopicRequest request, CancellationToken ct = default);
    Task<SubTopicDto> UpsertSubTopicAsync(Guid? id, UpsertSubTopicRequest request, CancellationToken ct = default);
    Task<LessonDetailDto> UpsertLessonAsync(Guid? id, UpsertLessonRequest request, CancellationToken ct = default);
    Task<LessonDetailDto> ReplaceLessonActivitiesAsync(Guid lessonId, IReadOnlyList<UpsertLessonActivityRequest> activities, CancellationToken ct = default);
    Task<PagedResult<AdminQuestionDto>> GetQuestionsAsync(AdminQuestionQuery query, CancellationToken ct = default);
    Task<AdminQuestionDto> GetQuestionAsync(Guid id, CancellationToken ct = default);
    Task<AdminQuestionDto> UpsertQuestionAsync(Guid? id, UpsertQuestionRequest request, CancellationToken ct = default);
    Task SetStatusAsync(string entity, Guid id, ContentStatus status, CancellationToken ct = default);
}

public class AdminService : IAdminService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly ICurriculumService _curriculum;

    public AdminService(IAppDbContext db, ICurrentUser current, IPasswordHasher hasher, IAuditService audit, ICurriculumService curriculum)
    {
        _db = db; _current = current; _hasher = hasher; _audit = audit; _curriculum = curriculum;
    }

    // ---------------- dashboard ----------------

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow; var today = now.Date; var week = today.AddDays(-7);
        var students = await _db.Students.Include(s => s.YearGroup).Select(s => new { s.YearGroup.Name }).ToListAsync(ct);
        var subjectPerf = await _db.StudentSubjectProgress.Where(p => p.SessionsCompleted > 0).GroupBy(p => p.Subject.Name)
            .Select(g => new CountByDto(g.Key, Math.Round(g.Average(p => p.AverageScore), 1))).ToListAsync(ct);
        var activity = await _db.StudySessions.Where(s => s.CompletedAt >= week).GroupBy(s => s.CompletedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var days = Enumerable.Range(0, 7).Select(i => week.AddDays(i + 1)).Select(d => new CountByDto(d.ToString("ddd dd MMM"), activity.FirstOrDefault(a => a.Day == d)?.Count ?? 0)).ToList();
        var allPerf = await _db.StudentSubjectProgress.Where(p => p.SessionsCompleted > 0).Select(p => p.AverageScore).ToListAsync(ct);

        return new AdminDashboardDto(
            students.Count,
            await _db.Parents.CountAsync(ct),
            await _db.Users.CountAsync(u => u.Role == UserRole.Admin, ct),
            await _db.Users.CountAsync(u => u.LastLoginAt >= week, ct),
            await _db.StudySessions.CountAsync(s => s.StartedAt >= today, ct),
            await _db.StudySessions.CountAsync(s => s.StartedAt >= week, ct),
            await _db.StudentAnswers.CountAsync(a => a.AnsweredAt >= today, ct),
            await _db.StudentAnswers.CountAsync(a => a.AnsweredAt >= week, ct),
            allPerf.Count == 0 ? 0 : Math.Round(allPerf.Average(), 1),
            await _db.StudentLessonProgress.CountAsync(l => l.PassedAt >= week, ct),
            await _db.Lessons.CountAsync(l => l.Status == ContentStatus.Published, ct),
            await _db.Questions.CountAsync(q => q.Status == ContentStatus.Published, ct),
            await _db.StudentTopicProgress.CountAsync(t => t.NextReviewAt <= now, ct),
            await _db.AIConversationMessages.CountAsync(m => m.Role == AiMessageRole.Student && m.CreatedAt >= week, ct),
            await _db.AuditLogs.CountAsync(a => !a.Success && a.CreatedAt >= now.AddHours(-24), ct),
            students.GroupBy(s => s.Name).OrderBy(g => g.Key).Select(g => new CountByDto(g.Key, g.Count())).ToList(),
            subjectPerf, days);
    }

    // ---------------- users ----------------

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(AdminUserQuery query, CancellationToken ct = default)
    {
        var q = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToUpperInvariant();
            q = q.Where(u => u.NormalizedEmail.Contains(s) || (u.FirstName + " " + u.LastName).ToUpper().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(query.Role) && Enum.TryParse<UserRole>(query.Role, true, out var role)) q = q.Where(u => u.Role == role);
        if (query.IsActive.HasValue) q = q.Where(u => u.IsActive == query.IsActive);
        var page = await q.OrderByDescending(u => u.CreatedAt).Select(u => u.Id).ToPagedResultAsync(new PagingQuery(query.Page, query.PageSize), ct);
        var items = new List<AdminUserDto>();
        foreach (var id in page.Items) items.Add(await GetUserAsync(id, ct));
        return new PagedResult<AdminUserDto> { Items = items, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    public async Task<AdminUserDto> GetUserAsync(Guid id, CancellationToken ct = default)
    {
        var u = await _db.Users.Include(x => x.Student).ThenInclude(s => s!.YearGroup).Include(x => x.Parent).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        List<string>? linked = null; int? childCount = null;
        if (u.Parent != null)
        {
            linked = await _db.StudentParents.Where(sp => sp.ParentId == u.Parent.Id).Select(sp => sp.Student.User.FirstName + " " + sp.Student.User.LastName).ToListAsync(ct);
            childCount = linked.Count;
        }
        else if (u.Student != null)
            linked = await _db.StudentParents.Where(sp => sp.StudentId == u.Student.Id).Select(sp => sp.Parent.User.FirstName + " " + sp.Parent.User.LastName).ToListAsync(ct);
        return new AdminUserDto(u.Id, u.Email, u.FirstName, u.LastName, u.Role.ToString(), u.EmailConfirmed, u.IsActive, u.MustChangePassword, u.LastLoginAt, u.CreatedAt,
            u.Student?.Id ?? u.Parent?.Id, u.Student?.YearGroup.Name, childCount, linked);
    }

    public async Task<AdminUserDto> CreateUserAsync(CreateAdminUserRequest request, CancellationToken ct = default)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) throw new ConflictException("EMAIL_ALREADY_REGISTERED", "An account with this email already exists.");
        if (!Enum.TryParse<UserRole>(request.Role, true, out var role) || role == UserRole.Student) throw new AppValidationException("role", "Role must be Admin or Parent. Students are created by their parent.");
        var user = new User
        {
            Email = request.Email.Trim(), NormalizedEmail = normalized, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
            PasswordHash = _hasher.Hash(request.Password), Role = role, EmailConfirmed = true, MustChangePassword = true,
            Parent = role == UserRole.Parent ? new Parent() : null
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.CreateUser", "User", user.Id.ToString(), new { user.Email, Role = role.ToString() }, true, ct);
        return await GetUserAsync(user.Id, ct);
    }

    public async Task<AdminUserDto> UpdateUserAsync(Guid id, AdminUpdateUserRequest request, CancellationToken ct = default)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (!string.IsNullOrWhiteSpace(request.FirstName)) u.FirstName = request.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(request.LastName)) u.LastName = request.LastName.Trim();
        if (request.IsActive.HasValue)
        {
            if (u.Id == _current.UserId && !request.IsActive.Value) throw new BusinessRuleException("CANNOT_DISABLE_SELF", "You cannot disable your own account.");
            u.IsActive = request.IsActive.Value;
        }
        if (request.EmailConfirmed.HasValue) u.EmailConfirmed = request.EmailConfirmed.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpdateUser", "User", id.ToString(), request, true, ct);
        return await GetUserAsync(id, ct);
    }

    public async Task SetPasswordAsync(Guid id, AdminSetPasswordRequest request, CancellationToken ct = default)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        u.PasswordHash = _hasher.Hash(request.NewPassword);
        u.MustChangePassword = request.MustChangePassword;
        u.FailedLoginCount = 0; u.LockoutEndAt = null;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.SetPassword", "User", id.ToString(), null, true, ct);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        var u = await _db.Users.Include(x => x.Parent).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (u.Id == _current.UserId) throw new BusinessRuleException("CANNOT_DELETE_SELF", "You cannot delete your own account.");
        if (u.Parent != null && await _db.StudentParents.AnyAsync(sp => sp.ParentId == u.Parent.Id, ct))
            throw new BusinessRuleException("PARENT_HAS_CHILDREN", "Disable the account instead: this parent still has linked children.");
        // Soft delete: anonymise and disable, keeping learning history referentially intact.
        u.IsActive = false;
        u.Email = $"deleted-{u.Id:N}@deleted.local"; u.NormalizedEmail = u.Email.ToUpperInvariant();
        u.FirstName = "Deleted"; u.LastName = "User"; u.PasswordHash = _hasher.Hash(Guid.NewGuid().ToString());
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.DeleteUser", "User", id.ToString(), null, true, ct);
    }

    // ---------------- audit & settings ----------------

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action.StartsWith(query.Action));
        if (query.UserId.HasValue) q = q.Where(a => a.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(a => (a.UserEmail ?? "").Contains(query.Search) || (a.Details ?? "").Contains(query.Search) || (a.EntityId ?? "").Contains(query.Search));
        if (query.From.HasValue) q = q.Where(a => a.CreatedAt >= query.From);
        if (query.To.HasValue) q = q.Where(a => a.CreatedAt <= query.To);
        if (query.Success.HasValue) q = q.Where(a => a.Success == query.Success);
        return await q.OrderByDescending(a => a.Id)
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.UserEmail, a.UserRole.HasValue ? a.UserRole.ToString() : null, a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress, a.Success, a.CreatedAt))
            .ToPagedResultAsync(new PagingQuery(query.Page, query.PageSize), ct);
    }

    public async Task<IReadOnlyList<SystemSettingDto>> GetSettingsAsync(CancellationToken ct = default) =>
        await _db.SystemSettings.OrderBy(s => s.Key).Select(s => new SystemSettingDto(s.Key, s.Value, s.Description, s.IsPublic, s.UpdatedAt)).ToListAsync(ct);

    public async Task<SystemSettingDto> UpdateSettingAsync(string key, UpdateSystemSettingRequest request, CancellationToken ct = default)
    {
        var s = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (s == null) { s = new SystemSetting { Key = key }; _db.SystemSettings.Add(s); }
        s.Value = request.Value; if (request.Description != null) s.Description = request.Description; s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpdateSetting", "SystemSetting", key, new { request.Value }, true, ct);
        return new SystemSettingDto(s.Key, s.Value, s.Description, s.IsPublic, s.UpdatedAt);
    }

    // ---------------- curriculum ----------------

    public async Task<TopicDto> UpsertTopicAsync(Guid? id, UpsertTopicRequest r, CancellationToken ct = default)
    {
        var qual = await _db.Qualifications.FirstOrDefaultAsync(q => q.Id == r.QualificationId, ct) ?? throw new NotFoundException("Qualification", r.QualificationId);
        Topic t;
        if (id.HasValue) t = await _db.Topics.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Topic", id);
        else { t = new Topic(); _db.Topics.Add(t); }
        if (await _db.Topics.AnyAsync(x => x.QualificationId == r.QualificationId && x.Code == r.Code && x.Id != t.Id, ct)) throw new ConflictException("DUPLICATE_CODE", "A topic with this code already exists in the qualification.");
        t.QualificationId = qual.Id; t.SubjectId = qual.SubjectId; t.Code = r.Code.Trim(); t.Name = r.Name.Trim(); t.Description = r.Description; t.SpecificationReference = r.SpecificationReference;
        t.SortOrder = r.SortOrder; t.ExamWeight = Math.Clamp(r.ExamWeight, 1, 10); t.Tier = ParseTier(r.Tier); t.Status = ParseStatus(r.Status, t.Status);
        t.YearGroupId = r.YearNumber.HasValue ? await _db.YearGroups.Where(y => y.Number == r.YearNumber).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct) : null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpsertTopic", "Topic", t.Id.ToString(), r, true, ct);
        return await _curriculum.GetTopicAsync(t.Id, ct);
    }

    public async Task<SubTopicDto> UpsertSubTopicAsync(Guid? id, UpsertSubTopicRequest r, CancellationToken ct = default)
    {
        var topic = await _db.Topics.Include(t => t.Qualification).FirstOrDefaultAsync(t => t.Id == r.TopicId, ct) ?? throw new NotFoundException("Topic", r.TopicId);
        SubTopic s;
        if (id.HasValue) s = await _db.SubTopics.Include(x => x.Mappings).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("SubTopic", id);
        else { s = new SubTopic(); _db.SubTopics.Add(s); }
        if (await _db.SubTopics.AnyAsync(x => x.TopicId == r.TopicId && x.Code == r.Code && x.Id != s.Id, ct)) throw new ConflictException("DUPLICATE_CODE", "A sub-topic with this code already exists in the topic.");
        s.TopicId = topic.Id; s.Code = r.Code.Trim(); s.Name = r.Name.Trim(); s.Description = r.Description; s.SpecificationReference = r.SpecificationReference;
        s.SortOrder = r.SortOrder; s.Tier = ParseTier(r.Tier); s.Status = ParseStatus(r.Status, s.Status);
        if (r.ReferenceBookTitle != null || r.ReferenceBookSection != null || r.MappingNotes != null)
        {
            var map = s.Mappings.FirstOrDefault(m => m.ExamBoardId == topic.Qualification.ExamBoardId);
            if (map == null) { map = new CurriculumMapping { SubTopicId = s.Id, ExamBoardId = topic.Qualification.ExamBoardId }; _db.CurriculumMappings.Add(map); if (!s.Mappings.Contains(map)) s.Mappings.Add(map); }
            map.SpecificationReference = r.SpecificationReference; map.ReferenceBookTitle = r.ReferenceBookTitle; map.ReferenceBookIsbn = r.ReferenceBookIsbn; map.ReferenceBookSection = r.ReferenceBookSection; map.Notes = r.MappingNotes;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpsertSubTopic", "SubTopic", s.Id.ToString(), r, true, ct);
        return await _curriculum.GetSubTopicAsync(s.Id, ct);
    }

    public async Task<LessonDetailDto> UpsertLessonAsync(Guid? id, UpsertLessonRequest r, CancellationToken ct = default)
    {
        if (!await _db.SubTopics.AnyAsync(s => s.Id == r.SubTopicId, ct)) throw new NotFoundException("SubTopic", r.SubTopicId);
        Lesson l;
        if (id.HasValue) l = await _db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson", id);
        else { l = new Lesson { CreatedByUserId = _current.UserId, Status = ContentStatus.Draft }; _db.Lessons.Add(l); }
        l.SubTopicId = r.SubTopicId; l.Title = r.Title.Trim(); l.Summary = r.Summary; l.ObjectivesJson = r.Objectives == null ? null : JsonSerializer.Serialize(r.Objectives);
        l.EstimatedMinutes = Math.Clamp(r.EstimatedMinutes, 5, 180); l.Difficulty = Math.Clamp(r.Difficulty, 1, 5); l.SortOrder = r.SortOrder; l.Tier = ParseTier(r.Tier); l.Status = ParseStatus(r.Status, l.Status);
        if (id.HasValue) l.Version++;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpsertLesson", "Lesson", l.Id.ToString(), new { r.Title, r.SubTopicId, r.Status }, true, ct);
        return await _curriculum.GetLessonAsync(l.Id, false, ct);
    }

    public async Task<LessonDetailDto> ReplaceLessonActivitiesAsync(Guid lessonId, IReadOnlyList<UpsertLessonActivityRequest> activities, CancellationToken ct = default)
    {
        var l = await _db.Lessons.Include(x => x.Activities).FirstOrDefaultAsync(x => x.Id == lessonId, ct) ?? throw new NotFoundException("Lesson", lessonId);
        var referenced = await _db.SessionActivities.Where(sa => sa.LessonActivityId != null && sa.LessonActivity!.LessonId == lessonId).Select(sa => sa.LessonActivityId!.Value).Distinct().ToListAsync(ct);
        // Keep activities referenced by sessions (update in place by position), remove the rest.
        var existing = l.Activities.OrderBy(a => a.SortOrder).ToList();
        var order = 0;
        var kept = new List<LessonActivity>();
        foreach (var a in activities.OrderBy(a => a.SortOrder))
        {
            order++;
            var target = existing.ElementAtOrDefault(order - 1) ?? new LessonActivity { LessonId = lessonId };
            if (target.Id == Guid.Empty || !l.Activities.Contains(target)) { _db.LessonActivities.Add(target); l.Activities.Add(target); }
            target.SortOrder = order; target.Type = Enum.TryParse<LessonActivityType>(a.Type, true, out var t) ? t : LessonActivityType.Explanation;
            target.Title = a.Title; target.ContentMarkdown = a.ContentMarkdown; target.QuestionId = a.QuestionId; target.EstimatedMinutes = a.EstimatedMinutes; target.IsCheckpoint = a.IsCheckpoint;
            if (a.QuestionId.HasValue && !await _db.Questions.AnyAsync(q => q.Id == a.QuestionId, ct)) throw new NotFoundException("Question", a.QuestionId);
            kept.Add(target);
        }
        foreach (var stale in existing.Where(e => !kept.Contains(e)))
        {
            if (referenced.Contains(stale.Id)) { stale.SortOrder = 1000 + stale.SortOrder; continue; } // orphaned but historical
            _db.LessonActivities.Remove(stale);
        }
        l.Version++;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.ReplaceLessonActivities", "Lesson", lessonId.ToString(), new { Count = activities.Count }, true, ct);
        return await _curriculum.GetLessonAsync(lessonId, false, ct);
    }

    public async Task<PagedResult<AdminQuestionDto>> GetQuestionsAsync(AdminQuestionQuery query, CancellationToken ct = default)
    {
        var q = _db.Questions.AsQueryable();
        if (query.SubjectId.HasValue) q = q.Where(x => x.SubTopic.Topic.SubjectId == query.SubjectId);
        if (query.TopicId.HasValue) q = q.Where(x => x.SubTopic.TopicId == query.TopicId);
        if (query.SubTopicId.HasValue) q = q.Where(x => x.SubTopicId == query.SubTopicId);
        if (query.LessonId.HasValue) q = q.Where(x => x.LessonId == query.LessonId);
        if (!string.IsNullOrWhiteSpace(query.Type) && Enum.TryParse<QuestionType>(query.Type, true, out var qt)) q = q.Where(x => x.QuestionType == qt);
        if (query.Difficulty.HasValue) q = q.Where(x => x.Difficulty == query.Difficulty);
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<ContentStatus>(query.Status, true, out var st)) q = q.Where(x => x.Status == st);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(x => x.QuestionText.Contains(query.Search) || (x.Tags ?? "").Contains(query.Search));
        var page = await q.OrderBy(x => x.SubTopic.Topic.Subject.SortOrder).ThenBy(x => x.SubTopic.Topic.SortOrder).ThenBy(x => x.SubTopic.SortOrder).ThenBy(x => x.Difficulty).Select(x => x.Id)
            .ToPagedResultAsync(new PagingQuery(query.Page, query.PageSize), ct);
        var items = new List<AdminQuestionDto>();
        foreach (var id in page.Items) items.Add(await GetQuestionAsync(id, ct));
        return new PagedResult<AdminQuestionDto> { Items = items, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    public async Task<AdminQuestionDto> GetQuestionAsync(Guid id, CancellationToken ct = default)
    {
        var q = await _db.Questions.Include(x => x.Options).Include(x => x.AcceptedAnswers).Include(x => x.MarkScheme)
            .Include(x => x.SubTopic).ThenInclude(s => s.Topic).ThenInclude(t => t.Subject).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question", id);
        var lessonTitle = q.LessonId.HasValue ? await _db.Lessons.Where(l => l.Id == q.LessonId).Select(l => l.Title).FirstOrDefaultAsync(ct) : null;
        var stats = await _db.StudentAnswers.Where(a => a.QuestionId == id && a.AttemptNumber == 1).GroupBy(a => a.QuestionId)
            .Select(g => new { Count = g.Count(), Correct = g.Count(a => a.IsCorrect) }).FirstOrDefaultAsync(ct);
        return new AdminQuestionDto(q.Id, q.SubTopicId, q.SubTopic.Name, q.SubTopic.Topic.Name, q.SubTopic.Topic.Subject.Name, q.LessonId, lessonTitle,
            q.QuestionType.ToString(), q.Difficulty, q.QuestionText, q.MaxMarks, q.IsExamStyle, q.Tier.ToString(), q.Hint, q.Explanation, q.ImageUrl, q.TimeLimitSeconds, q.Tags, q.Status.ToString(),
            ProgressService.ParseJson(q.MetadataJson),
            q.Options.OrderBy(o => o.SortOrder).Select(o => new QuestionOptionInput(o.Text, o.IsCorrect, o.SortOrder, o.MatchKey, o.Feedback)).ToList(),
            q.AcceptedAnswers.Select(a => new QuestionAnswerInput(a.AnswerText, a.IsCaseSensitive, a.NumericValue, a.NumericTolerance, a.Unit, a.BlankIndex, a.Marks)).ToList(),
            q.MarkScheme.OrderBy(m => m.SortOrder).Select(m => new MarkSchemeInput(m.SortOrder, m.CriterionText, m.Marks, ParseKeywords(m.KeywordsJson))).ToList(),
            stats?.Count ?? 0, stats == null || stats.Count == 0 ? null : Math.Round(stats.Correct * 100m / stats.Count, 1));
    }

    public async Task<AdminQuestionDto> UpsertQuestionAsync(Guid? id, UpsertQuestionRequest r, CancellationToken ct = default)
    {
        if (!await _db.SubTopics.AnyAsync(s => s.Id == r.SubTopicId, ct)) throw new NotFoundException("SubTopic", r.SubTopicId);
        if (!Enum.TryParse<QuestionType>(r.QuestionType, true, out var type)) throw new AppValidationException("questionType", "Unknown question type.");
        Question q;
        if (id.HasValue) q = await _db.Questions.Include(x => x.Options).Include(x => x.AcceptedAnswers).Include(x => x.MarkScheme).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question", id);
        else { q = new Question(); _db.Questions.Add(q); }
        q.SubTopicId = r.SubTopicId; q.LessonId = r.LessonId; q.QuestionType = type; q.Difficulty = Math.Clamp(r.Difficulty, 1, 5); q.QuestionText = r.QuestionText; q.MaxMarks = Math.Max(1, r.MaxMarks);
        q.IsExamStyle = r.IsExamStyle; q.Tier = ParseTier(r.Tier); q.Hint = r.Hint; q.Explanation = r.Explanation; q.ImageUrl = r.ImageUrl; q.TimeLimitSeconds = r.TimeLimitSeconds; q.Tags = r.Tags;
        q.Status = ParseStatus(r.Status, id.HasValue ? q.Status : ContentStatus.Published);
        q.MetadataJson = r.Metadata.HasValue && r.Metadata.Value.ValueKind == JsonValueKind.Object ? r.Metadata.Value.GetRawText() : q.MetadataJson;
        q.ExamBoardId ??= await _db.SubTopics.Where(s => s.Id == r.SubTopicId).Select(s => s.Topic.Qualification.ExamBoardId).FirstAsync(ct);

        _db.QuestionOptions.RemoveRange(q.Options); q.Options.Clear();
        foreach (var o in r.Options ?? Array.Empty<QuestionOptionInput>()) { var e = new QuestionOption { QuestionId = q.Id, Text = o.Text, IsCorrect = o.IsCorrect, SortOrder = o.SortOrder, MatchKey = o.MatchKey, Feedback = o.Feedback }; _db.QuestionOptions.Add(e); if (!q.Options.Contains(e)) q.Options.Add(e); }
        _db.QuestionAnswers.RemoveRange(q.AcceptedAnswers); q.AcceptedAnswers.Clear();
        foreach (var a in r.Answers ?? Array.Empty<QuestionAnswerInput>()) { var e = new QuestionAnswer { QuestionId = q.Id, AnswerText = a.AnswerText, IsCaseSensitive = a.IsCaseSensitive, NumericValue = a.NumericValue, NumericTolerance = a.NumericTolerance, Unit = a.Unit, BlankIndex = a.BlankIndex, Marks = Math.Max(1, a.Marks) }; _db.QuestionAnswers.Add(e); if (!q.AcceptedAnswers.Contains(e)) q.AcceptedAnswers.Add(e); }
        _db.MarkSchemes.RemoveRange(q.MarkScheme); q.MarkScheme.Clear();
        foreach (var m in r.MarkScheme ?? Array.Empty<MarkSchemeInput>()) { var e = new MarkScheme { QuestionId = q.Id, SortOrder = m.SortOrder, CriterionText = m.CriterionText, Marks = Math.Max(1, m.Marks), KeywordsJson = m.Keywords == null ? null : JsonSerializer.Serialize(m.Keywords) }; _db.MarkSchemes.Add(e); if (!q.MarkScheme.Contains(e)) q.MarkScheme.Add(e); }

        ValidateQuestion(q);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpsertQuestion", "Question", q.Id.ToString(), new { r.QuestionType, r.SubTopicId, r.Status }, true, ct);
        return await GetQuestionAsync(q.Id, ct);
    }

    public async Task SetStatusAsync(string entity, Guid id, ContentStatus status, CancellationToken ct = default)
    {
        switch (entity.ToLowerInvariant())
        {
            case "topic": (await _db.Topics.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Topic", id)).Status = status; break;
            case "subtopic": (await _db.SubTopics.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("SubTopic", id)).Status = status; break;
            case "lesson": (await _db.Lessons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lesson", id)).Status = status; break;
            case "question": (await _db.Questions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question", id)).Status = status; break;
            default: throw new AppValidationException("entity", "Entity must be topic, subtopic, lesson or question.");
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.SetStatus", entity, id.ToString(), new { Status = status.ToString() }, true, ct);
    }

    private static void ValidateQuestion(Question q)
    {
        var errors = new Dictionary<string, string[]>();
        switch (q.QuestionType)
        {
            case QuestionType.MultipleChoice or QuestionType.TrueFalse:
                if (q.Options.Count(o => o.IsCorrect) != 1) errors["options"] = new[] { "Exactly one option must be correct." }; break;
            case QuestionType.MultipleAnswer:
                if (!q.Options.Any(o => o.IsCorrect)) errors["options"] = new[] { "At least one option must be correct." }; break;
            case QuestionType.SingleAnswer or QuestionType.FillInTheBlank or QuestionType.NumericalAnswer or QuestionType.FormulaCalculation or QuestionType.Equation:
                if (q.AcceptedAnswers.Count == 0) errors["answers"] = new[] { "At least one accepted answer is required." }; break;
            case QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.ExamQuestion:
                if (q.MarkScheme.Count == 0) errors["markScheme"] = new[] { "A mark scheme is required for written answers." }; break;
            case QuestionType.Matching or QuestionType.DragAndDrop or QuestionType.DiagramLabelling or QuestionType.Ordering:
                if (q.Options.Count < 2 || q.Options.Any(o => string.IsNullOrWhiteSpace(o.MatchKey))) errors["options"] = new[] { "At least two options, each with a matchKey (target or position), are required." }; break;
        }
        if (errors.Count > 0) throw new AppValidationException(errors);
    }

    private static IReadOnlyList<IReadOnlyList<string>>? ParseKeywords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<List<string>>>(json); } catch { return null; }
    }
    private static Tier ParseTier(string? s) => Enum.TryParse<Tier>(s, true, out var t) ? t : Tier.NotApplicable;
    private static ContentStatus ParseStatus(string? s, ContentStatus fallback) => Enum.TryParse<ContentStatus>(s, true, out var t) ? t : fallback;
}
