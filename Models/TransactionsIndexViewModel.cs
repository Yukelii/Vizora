namespace Vizora.Models
{
    public class TransactionsIndexViewModel
    {
        public IReadOnlyList<TransactionListItemViewModel> Transactions { get; set; } =
            Array.Empty<TransactionListItemViewModel>();

        public IReadOnlyList<CategoryFilterOptionViewModel> CategoryOptions { get; set; } = Array.Empty<CategoryFilterOptionViewModel>();

        public string? Search { get; set; }

        public int? Category { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public decimal? MinAmount { get; set; }

        public decimal? MaxAmount { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public int TotalCount { get; set; }

        public int TotalPages => TotalCount <= 0
            ? 1
            : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public bool HasPreviousPage => Page > 1;

        public bool HasNextPage => Page < TotalPages;
    }

    public class CategoryFilterOptionViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
