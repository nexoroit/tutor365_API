using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IStudyPlanService
{
    Task<StudyPlanDto> CreateAsync(Guid studentId, CreateStudyPlanRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StudyPlanDto>> GetForStudentAsync(Guid studentId, bool includeInactive, CancellationToken ct = default);
    Task<StudyPlanDto> GetAsync(Guid planId, CancellationToken ct = default);
    Task CancelAsync(Guid planId, CancellationToken ct = default);
    Task<StudyPlanDto> CancelItemAsync(Guid planId, Guid itemId, CancellationToken ct = default);
}

public class StudyPlanService : IStudyPlanService
{
    private readonly IAppDbContext _db;
    private readonly IAccessService _access;
    private readonly ICurrentUser _current;
    private readonly INotificationService _notifications;
    private readonly IStudentService _students;

    public StudyPlanService(IAppDbContext db, IAccessService access, ICurrentUser current, INotificationService notifications, IStudentService students)
    {
        _db = db; _access = access; _current = current; _notifications = notifications; _students = students;
    }

    public async Task<StudyPlanDto> CreateAsync(Guid studentId, CreateStudyPlanRequest request, CancellationToken ct = default)
    {
        if (_current.Role is not (UserRole.Parent or UserRole.Admin)) throw new ForbiddenException();
        var student = await _access.GetAccessibleStudentAsync(studentId, true, ct);
        if (request.Items.Count == 0) throw new AppValidationException("items", "Add at least one item to the plan.");

        var plan = new StudyPlan { StudentId = studentId, CreatedByUserId = _current.UserId, Title = request.Title.Trim(), Notes = request.Notes, StartDate = request.StartDate, EndDate = request.EndDate };
        var order = 0;
        foreach (var i in request.Items)
        {
            if (!await _db.Subjects.AnyAsync(s => s.Id == i.SubjectId, ct)) throw new NotFoundException("Subject", i.SubjectId);
            if (i.TopicId.HasValue && !await _db.Topics.AnyAsync(t => t.Id == i.TopicId && t.SubjectId == i.SubjectId, ct)) throw new NotFoundException("Topic", i.TopicId);
            if (i.LessonId.HasValue && !await _db.Lessons.AnyAsync(l => l.Id == i.LessonId && l.SubTopic.Topic.SubjectId == i.SubjectId, ct)) throw new NotFoundException("Lesson", i.LessonId);
            var itemType = Enum.TryParse<StudyPlanItemType>(i.ItemType, true, out var it) ? it : i.LessonId.HasValue ? StudyPlanItemType.Lesson : i.TopicId.HasValue ? StudyPlanItemType.TopicReview : StudyPlanItemType.Lesson;
            if (itemType == StudyPlanItemType.Lesson && !i.LessonId.HasValue) throw new AppValidationException("lessonId", "Choose a lesson for a lesson item.");
            if (itemType is StudyPlanItemType.TopicReview or StudyPlanItemType.TopicTest && !i.TopicId.HasValue) throw new AppValidationException("topicId", "Choose a topic for a review or test item.");
            plan.Items.Add(new StudyPlanItem
            {
                SubjectId = i.SubjectId, TopicId = i.TopicId, LessonId = itemType == StudyPlanItemType.Lesson ? i.LessonId : null, DueDate = i.DueDate, QuestionCount = i.QuestionCount, Notes = i.Notes, SortOrder = ++order,
                ItemType = itemType, Priority = Enum.TryParse<StudyPlanPriority>(i.Priority, true, out var p) ? p : StudyPlanPriority.Normal
            });
        }
        _db.StudyPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        await _notifications.NotifyAsync(student.UserId, NotificationType.WorkAssigned, "New work assigned",
            $"\"{plan.Title}\" has been added to your study plan with {plan.Items.Count} item{(plan.Items.Count == 1 ? "" : "s")}.", new { planId = plan.Id }, ct);
        return await GetAsync(plan.Id, ct);
    }

    public async Task<IReadOnlyList<StudyPlanDto>> GetForStudentAsync(Guid studentId, bool includeInactive, CancellationToken ct = default)
    {
        if (!await _access.CanAccessStudentAsync(studentId, ct)) throw new ForbiddenException();
        var q = _db.StudyPlans.Where(p => p.StudentId == studentId);
        if (!includeInactive) q = q.Where(p => p.Status == StudyPlanStatus.Active);
        var ids = await q.OrderByDescending(p => p.CreatedAt).Select(p => p.Id).ToListAsync(ct);
        var list = new List<StudyPlanDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    public async Task<StudyPlanDto> GetAsync(Guid planId, CancellationToken ct = default)
    {
        var plan = await _db.StudyPlans.Include(p => p.Student).ThenInclude(s => s.User).FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException("StudyPlan", planId);
        if (!await _access.CanAccessStudentAsync(plan.StudentId, ct)) throw new ForbiddenException();
        var items = (await _students.GetAssignedWorkAsync(plan.StudentId, true, ct)).Where(i => i.StudyPlanId == planId).ToList();
        return new StudyPlanDto(plan.Id, plan.StudentId, plan.Student.User.FullName, plan.Title, plan.Notes, plan.StartDate, plan.EndDate, plan.Status.ToString(), plan.IsSystemGenerated,
            plan.CreatedByUserId, plan.CreatedAt, items, items.Count(i => i.Status == "Completed"), items.Count);
    }

    public async Task CancelAsync(Guid planId, CancellationToken ct = default)
    {
        if (_current.Role is not (UserRole.Parent or UserRole.Admin)) throw new ForbiddenException();
        var plan = await _db.StudyPlans.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException("StudyPlan", planId);
        if (!await _access.CanAccessStudentAsync(plan.StudentId, ct)) throw new ForbiddenException();
        plan.Status = StudyPlanStatus.Cancelled;
        foreach (var i in plan.Items.Where(i => i.Status is StudyPlanItemStatus.Pending or StudyPlanItemStatus.InProgress or StudyPlanItemStatus.Overdue)) i.Status = StudyPlanItemStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<StudyPlanDto> CancelItemAsync(Guid planId, Guid itemId, CancellationToken ct = default)
    {
        if (_current.Role is not (UserRole.Parent or UserRole.Admin)) throw new ForbiddenException();
        var item = await _db.StudyPlanItems.Include(i => i.StudyPlan).FirstOrDefaultAsync(i => i.Id == itemId && i.StudyPlanId == planId, ct) ?? throw new NotFoundException("StudyPlanItem", itemId);
        if (!await _access.CanAccessStudentAsync(item.StudyPlan.StudentId, ct)) throw new ForbiddenException();
        if (item.Status != StudyPlanItemStatus.Completed) item.Status = StudyPlanItemStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(planId, ct);
    }
}
