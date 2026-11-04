namespace MedSystem.Core;

public static class Pagination
{
    public const int DefaultPageSize = 100;
    public const int MaximumPageSize = 200;

    public static int GetTotalPages(long totalCount, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

    public static int ClampPage(int page, long totalCount, int pageSize) =>
        Math.Clamp(page, 1, GetTotalPages(totalCount, pageSize));
}
