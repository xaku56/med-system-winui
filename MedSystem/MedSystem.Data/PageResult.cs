namespace MedSystem.Data;

public sealed class PageResult<T>
{
    public required List<T> Items { get; init; }
    public long TotalCount { get; init; }
    public int Page { get; init; }
}
