using Microsoft.EntityFrameworkCore;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;

namespace Vizora.Services
{
    public interface IFinanceAnalyticsService
    {
        Task<DashboardViewModel> GetDashboardStatisticsAsync(
            string? filter = null,
            DateTime? startDate = null,
            DateTime? endDate = null);

        Task<IReadOnlyList<CategorySpendingDto>> GetCategorySpendingAsync(
            int limit = 5,
            DateTime? startDate = null,
            DateTime? endDate = null);

        Task<IReadOnlyList<BudgetProgressDto>> GetBudgetProgressAsync(
            DateTime? startDate = null,
            DateTime? endDate = null);
    }

    public class FinanceAnalyticsService : IFinanceAnalyticsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IBudgetService _budgetService;
        private readonly IFinancialInsightsService _financialInsightsService;

        public FinanceAnalyticsService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IBudgetService budgetService,
            IFinancialInsightsService financialInsightsService)
        {
            _context = context;
            _userContextService = userContextService;
            _budgetService = budgetService;
            _financialInsightsService = financialInsightsService;
        }

        public async Task<DashboardViewModel> GetDashboardStatisticsAsync(
            string? filter = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var userId = _userContextService.GetRequiredUserId();
            var nowUtc = DateTime.UtcNow;

            filter ??= "all";

            // Apply a preset range only when a custom range is not supplied.
            if (!startDate.HasValue && !endDate.HasValue)
            {
                startDate = filter switch
                {
                    "7days" => nowUtc.AddDays(-7),
                    "30days" => nowUtc.AddDays(-30),
                    "month" => new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                    "year" => new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    _ => null
                };
            }
            else
            {
                filter = "custom";
            }

            // All dashboard metrics must remain strictly user-scoped.
            var transactions = _context.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId);
            transactions = ApplyDateRange(transactions, startDate, endDate);

            var totalIncome = await transactions
                .Where(t => t.Type == TransactionType.Income)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            var totalExpense = await transactions
                .Where(t => t.Type == TransactionType.Expense)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            var totalTransactions = await transactions.CountAsync();
            var topSpendingCategories = await GetCategorySpendingAsync(userId, limit: 5, startDate, endDate);
            var budgetProgress = await GetBudgetProgressAsync(userId, startDate, endDate);
            var insights = await _financialInsightsService.GetInsightsAsync();

            // Build a compact month-series for trend charts and summary table output.
            var monthlyRollup = await transactions
                .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Income = g.Sum(t => t.Type == TransactionType.Income ? t.Amount : 0m),
                    Expense = g.Sum(t => t.Type == TransactionType.Expense ? t.Amount : 0m)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            // Limit recent rows to keep dashboard payload light.
            var recentTransactions = await transactions
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new RecentTransactionViewModel
                {
                    TransactionId = t.Id,
                    CategoryName = t.Category != null ? t.Category.Name : "Uncategorized",
                    Type = t.Type,
                    Amount = t.Amount,
                    Description = t.Description ?? string.Empty,
                    TransactionDate = t.TransactionDate
                })
                .ToListAsync();

            // Budget analytics are calculated in the service layer for reuse across dashboard/reporting surfaces.
            var budgetPerformance = await _budgetService.GetAllWithPerformanceAsync(startDate, endDate);

            return new DashboardViewModel
            {
                SelectedFilter = filter,
                StartDate = startDate,
                EndDate = endDate,

                TotalIncome = totalIncome,
                TotalExpense = totalExpense,
                NetCashFlow = totalIncome - totalExpense,
                TotalTransactions = totalTransactions,

                SpendingByCategoryLabels = topSpendingCategories.Select(x => x.CategoryName).ToList(),
                SpendingByCategoryAmounts = topSpendingCategories.Select(x => x.TotalAmount).ToList(),
                TopSpendingCategories = topSpendingCategories.ToList(),
                BudgetProgress = budgetProgress.ToList(),
                Insights = insights.ToList(),

                MonthlyLabels = monthlyRollup.Select(x => $"{x.Year}-{x.Month:D2}").ToList(),
                MonthlyIncomeData = monthlyRollup.Select(x => x.Income).ToList(),
                MonthlyExpenseData = monthlyRollup.Select(x => x.Expense).ToList(),

                MonthlySummaries = monthlyRollup
                    .Select(x => new MonthlySummaryViewModel
                    {
                        MonthLabel = $"{x.Year}-{x.Month:D2}",
                        Income = x.Income,
                        Expense = x.Expense
                    })
                    .ToList(),

                RecentTransactions = recentTransactions,
                BudgetPerformance = budgetPerformance.Take(10).ToList()
            };
        }

        public async Task<IReadOnlyList<CategorySpendingDto>> GetCategorySpendingAsync(
            int limit = 5,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var userId = _userContextService.GetRequiredUserId();
            return await GetCategorySpendingAsync(userId, limit, startDate, endDate);
        }

        private async Task<IReadOnlyList<CategorySpendingDto>> GetCategorySpendingAsync(
            string userId,
            int limit,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var safeLimit = limit <= 0 ? 5 : Math.Min(limit, 25);

            var expenseTransactions = _context.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.Type == TransactionType.Expense &&
                    t.Category != null);
            expenseTransactions = ApplyDateRange(expenseTransactions, startDate, endDate);

            return await expenseTransactions
                .GroupBy(t => new
                {
                    t.CategoryId,
                    CategoryName = t.Category!.Name
                })
                .Select(g => new CategorySpendingDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    TotalAmount = g.Sum(t => t.Amount),
                    TransactionCount = g.Count()
                })
                .OrderByDescending(x => x.TotalAmount)
                .ThenBy(x => x.CategoryName)
                .Take(safeLimit)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<BudgetProgressDto>> GetBudgetProgressAsync(
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var userId = _userContextService.GetRequiredUserId();
            return await GetBudgetProgressAsync(userId, startDate, endDate);
        }

        private async Task<IReadOnlyList<BudgetProgressDto>> GetBudgetProgressAsync(
            string userId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var normalizedStart = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
                : (DateTime?)null;
            var normalizedEnd = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : (DateTime?)null;

            var budgetsQuery = _context.Budgets
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.BudgetPeriod != null);

            if (normalizedStart.HasValue)
            {
                budgetsQuery = budgetsQuery.Where(b => b.BudgetPeriod!.EndDate >= normalizedStart.Value);
            }

            if (normalizedEnd.HasValue)
            {
                budgetsQuery = budgetsQuery.Where(b => b.BudgetPeriod!.StartDate <= normalizedEnd.Value);
            }

            var rows = await budgetsQuery
                .OrderByDescending(b => b.BudgetPeriod!.StartDate)
                .ThenBy(b => b.Category!.Name)
                .Select(b => new
                {
                    BudgetId = b.Id,
                    b.CategoryId,
                    CategoryName = b.Category != null ? b.Category.Name : "Uncategorized",
                    BudgetAmount = b.PlannedAmount,
                    ActualSpent = _context.Transactions
                        .Where(t =>
                            t.UserId == userId &&
                            t.Type == TransactionType.Expense &&
                            t.CategoryId == b.CategoryId &&
                            t.TransactionDate >= b.BudgetPeriod!.StartDate &&
                            t.TransactionDate <= b.BudgetPeriod!.EndDate &&
                            (!normalizedStart.HasValue || t.TransactionDate >= normalizedStart.Value) &&
                            (!normalizedEnd.HasValue || t.TransactionDate <= normalizedEnd.Value))
                        .Sum(t => (decimal?)t.Amount) ?? 0m
                })
                .Take(10)
                .ToListAsync();

            return rows
                .Select(row =>
                {
                    var remainingAmount = row.BudgetAmount - row.ActualSpent;
                    var percentageUsed = row.BudgetAmount <= 0m
                        ? 0m
                        : Math.Round((row.ActualSpent / row.BudgetAmount) * 100m, 2);

                    return new BudgetProgressDto
                    {
                        BudgetId = row.BudgetId,
                        CategoryId = row.CategoryId,
                        CategoryName = row.CategoryName,
                        BudgetAmount = row.BudgetAmount,
                        ActualSpent = row.ActualSpent,
                        RemainingAmount = remainingAmount,
                        PercentageUsed = percentageUsed
                    };
                })
                .ToList();
        }

        private static IQueryable<Transaction> ApplyDateRange(
            IQueryable<Transaction> query,
            DateTime? startDate,
            DateTime? endDate)
        {
            if (startDate.HasValue)
            {
                var start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(t => t.TransactionDate >= start);
            }

            if (endDate.HasValue)
            {
                var end = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                query = query.Where(t => t.TransactionDate <= end);
            }

            return query;
        }
    }
}
