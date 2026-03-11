using Vizora.DTOs;

namespace Vizora.Models
{
    public class DashboardViewModel
    {
        public string SelectedFilter { get; set; } = "all";

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal NetCashFlow { get; set; }
        public int TotalTransactions { get; set; }

        public List<string> SpendingByCategoryLabels { get; set; } = new();
        public List<decimal> SpendingByCategoryAmounts { get; set; } = new();
        public List<CategorySpendingDto> TopSpendingCategories { get; set; } = new();

        public List<string> MonthlyLabels { get; set; } = new();
        public List<decimal> MonthlyIncomeData { get; set; } = new();
        public List<decimal> MonthlyExpenseData { get; set; } = new();

        public List<BudgetPerformanceViewModel> BudgetPerformance { get; set; } = new();
        public List<BudgetProgressDto> BudgetProgress { get; set; } = new();
        public List<FinancialInsightDto> Insights { get; set; } = new();

        public List<MonthlySummaryViewModel> MonthlySummaries { get; set; } = new();
        public List<RecentTransactionViewModel> RecentTransactions { get; set; } = new();
    }

    public class MonthlySummaryViewModel
    {
        public string MonthLabel { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Net => Income - Expense;
    }

    public class RecentTransactionViewModel
    {
        public int TransactionId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public TransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
    }
}
