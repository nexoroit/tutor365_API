using Microsoft.EntityFrameworkCore;

namespace Tutor365.Application.Common;

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagingQuery paging, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);
        return new PagedResult<T> { Items = items, Page = paging.SafePage, PageSize = paging.SafePageSize, TotalCount = total };
    }
}

public static class GradeHelper
{
    /// <summary>Approximate GCSE 9-1 grade from a percentage. Boundaries are indicative and can be tuned in SystemSettings.</summary>
    public static int EstimateGrade(decimal percent) => percent switch
    {
        >= 90 => 9, >= 80 => 8, >= 70 => 7, >= 60 => 6, >= 50 => 5,
        >= 40 => 4, >= 30 => 3, >= 20 => 2, >= 10 => 1, _ => 0
    };

    /// <summary>Percentage a student should be scoring to be on track for a target grade.</summary>
    public static int RequiredPercentForGrade(int grade) => Math.Clamp(grade, 1, 9) switch
    {
        9 => 90, 8 => 80, 7 => 70, 6 => 60, 5 => 50, 4 => 40, 3 => 30, 2 => 20, _ => 10
    };
}
