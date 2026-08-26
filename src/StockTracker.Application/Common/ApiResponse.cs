namespace StockTracker.Application.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ApiError? Error { get; set; }
    public string? CorrelationId { get; set; }

    public static ApiResponse<T> Ok(T data, string? correlationId = null) =>
        new() { Success = true, Data = data, Error = null, CorrelationId = correlationId };

    public static ApiResponse<T> Fail(string code, string message, Dictionary<string, string[]>? details = null, string? correlationId = null) =>
        new()
        {
            Success = false,
            Data = default,
            Error = new ApiError { Code = code, Message = message, Details = details },
            CorrelationId = correlationId
        };
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Details { get; set; }
}

public class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public PagedResponse() { }

    public PagedResponse(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page < 1 ? 1 : page;
        PageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);
    }
}

public class PaginationParams
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : (value < 1 ? 1 : value);
    }
}
