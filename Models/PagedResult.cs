namespace Vizora.Models
{
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        public int TotalCount { get; init; }

        public int Page { get; init; } = 1;

        public int PageSize { get; init; } = 20;

        public int TotalPages => TotalCount <= 0
            ? 1
            : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
