namespace Vizora.DTOs
{
    public class BudgetProgressDto
    {
        public int BudgetId { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public decimal BudgetAmount { get; set; }

        public decimal ActualSpent { get; set; }

        public decimal RemainingAmount { get; set; }

        public decimal PercentageUsed { get; set; }
    }
}
