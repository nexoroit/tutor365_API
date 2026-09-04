using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public class AccessService : IAccessService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _user;

    public AccessService(IAppDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<Student> GetAccessibleStudentAsync(Guid studentId, bool includeProfile = false, CancellationToken ct = default)
    {
        if (!await CanAccessStudentAsync(studentId, ct))
            throw new ForbiddenException("You do not have access to this student.");

        IQueryable<Student> q = _db.Students;
        if (includeProfile)
            q = q.Include(s => s.User).Include(s => s.YearGroup).Include(s => s.ExamBoard)
                 .Include(s => s.SubjectSettings).ThenInclude(ss => ss.Subject)
                 .Include(s => s.Schedule);
        return await q.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
    }

    public async Task<bool> CanAccessStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var userId = _user.RequireUserId();
        return _user.Role switch
        {
            UserRole.Admin => await _db.Students.AnyAsync(s => s.Id == studentId, ct),
            UserRole.Student => await _db.Students.AnyAsync(s => s.Id == studentId && s.UserId == userId, ct),
            UserRole.Parent => await _db.StudentParents.AnyAsync(sp => sp.StudentId == studentId && sp.Parent.UserId == userId, ct),
            _ => false
        };
    }

    public async Task<Guid> GetCurrentStudentIdAsync(CancellationToken ct = default)
    {
        var userId = _user.RequireUserId();
        var id = await _db.Students.Where(s => s.UserId == userId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new ForbiddenException("Current user is not a student.");
    }

    public async Task<Guid> GetCurrentParentIdAsync(CancellationToken ct = default)
    {
        var userId = _user.RequireUserId();
        var id = await _db.Parents.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new ForbiddenException("Current user is not a parent.");
    }

}
