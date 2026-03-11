namespace Vizora.DTOs
{
    public class CategorySpendingDto
    {
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }

        public int TransactionCount { get; set; }
    }
}
