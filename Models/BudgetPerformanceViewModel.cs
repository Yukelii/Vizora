namespace Vizora.Models
{
    public class BudgetPerformanceViewModel
    {
        public int BudgetId { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public BudgetPeriodType PeriodType { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public decimal PlannedAmount { get; set; }

        public decimal ActualSpending { get; set; }

        public decimal RemainingAmount { get; set; }

        public decimal UsagePercent { get; set; }

        public bool IsOverBudget => ActualSpending > PlannedAmount;
    }
}
