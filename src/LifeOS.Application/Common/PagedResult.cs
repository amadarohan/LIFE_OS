namespace LifeOS.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int pageSize) => new([], 0, 1, pageSize);
}

public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > 200 ? 50 : PageSize;
    public int Skip => (SafePage - 1) * SafePageSize;
}
