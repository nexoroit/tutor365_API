using System.Collections.Concurrent;

namespace Tutor365.Application.Common;

/// <summary>In-process serialisation of write operations per student (plan generation, session start), so concurrent
/// requests from the same client don't race on unique indexes. A single API process is assumed; the DB unique indexes remain the backstop.</summary>
public static class StudentLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static async Task<T> RunAsync<T>(Guid studentId, Func<Task<T>> action, CancellationToken ct = default)
    {
        var sem = Locks.GetOrAdd(studentId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try { return await action(); }
        finally { sem.Release(); }
    }
}
