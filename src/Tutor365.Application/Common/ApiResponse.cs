namespace Tutor365.Application.Common;

/// <summary>Uniform JSON envelope returned by every endpoint.</summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public IDictionary<string, string[]>? Errors { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse Ok(string? message = null) => new() { Success = true, Message = message };
    public static ApiResponse Fail(string errorCode, string message, IDictionary<string, string[]>? errors = null)
        => new() { Success = false, ErrorCode = errorCode, Message = message, Errors = errors };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }
    public static ApiResponse<T> Ok(T data, string? message = null) => new() { Success = true, Data = data, Message = message };
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public record PagingQuery(int Page = 1, int PageSize = 20)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 20 : Math.Min(PageSize, 200);
    public int Skip => (SafePage - 1) * SafePageSize;
}
