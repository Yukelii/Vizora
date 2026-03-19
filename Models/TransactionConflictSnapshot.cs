namespace Vizora.Models
{
    public sealed class TransactionConflictSnapshot
    {
        public string RowVersionHex { get; set; } = string.Empty;

        public int CategoryId { get; set; }

        public decimal Amount { get; set; }

        public string? Description { get; set; }

        public DateTime TransactionDate { get; set; }
    }
}
