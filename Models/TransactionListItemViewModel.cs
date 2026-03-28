namespace Vizora.Models
{
    public sealed class TransactionListItemViewModel
    {
        public int Id { get; init; }

        public int CategoryId { get; init; }

        public CategoryPresentationViewModel CategoryPresentation { get; init; } =
            CategoryPresentationViewModel.FromCategory(null);

        public decimal Amount { get; init; }

        public TransactionType Type { get; init; }

        public string? Description { get; init; }

        public DateTime TransactionDate { get; init; }

        public static TransactionListItemViewModel FromTransaction(Transaction transaction)
        {
            return new TransactionListItemViewModel
            {
                Id = transaction.Id,
                CategoryId = transaction.CategoryId,
                CategoryPresentation = CategoryPresentationViewModel.FromCategory(
                    transaction.Category,
                    transaction.Category?.Name ?? "Uncategorized"),
                Amount = transaction.Amount,
                Type = transaction.Type,
                Description = transaction.Description,
                TransactionDate = transaction.TransactionDate
            };
        }
    }
}
