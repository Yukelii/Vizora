using Microsoft.EntityFrameworkCore;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Enums;
using Vizora.Models;

namespace Vizora.Services
{
    public interface IFinancialInsightsService
    {
        Task<IReadOnlyList<FinancialInsightDto>> GetInsightsAsync();
    }

    public class FinancialInsightsService : IFinancialInsightsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;

        public FinancialInsightsService(
            ApplicationDbContext context,
            IUserContextService userContextService)
        {
            _context = context;
            _userContextService = userContextService;
        }

        public async Task<IReadOnlyList<FinancialInsightDto>> GetInsightsAsync()
        {
            var userId = _userContextService.GetRequiredUserId();
            var nowUtc = DateTime.UtcNow;
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            var previousMonthStart = monthStart.AddMonths(-1);
            var previousMonthEnd = monthStart.AddTicks(-1);

            var insights = new List<FinancialInsightDto>();

            await AddTopSpendingCategoryInsightAsync(insights, userId, monthStart, monthEnd, nowUtc);
            await AddMonthlySpendingChangeInsightAsync(insights, userId, monthStart, monthEnd, previousMonthStart, previousMonthEnd, nowUtc);
            await AddBudgetRiskInsightsAsync(insights, userId, nowUtc);
            await AddSubscriptionInsightAsync(insights, userId, nowUtc);

            return insights
                .OrderByDescending(i => i.Severity)
                .ThenByDescending(i => i.CreatedAt)
                .ToList();
        }

        private async Task AddTopSpendingCategoryInsightAsync(
            ICollection<FinancialInsightDto> insights,
            string userId,
            DateTime monthStart,
            DateTime monthEnd,
            DateTime createdAt)
        {
            var totalMonthlyExpense = await _context.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.Type == TransactionType.Expense &&
                    t.TransactionDate >= monthStart &&
                    t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            if (totalMonthlyExpense <= 0m)
            {
                return;
            }

            var topCategory = await _context.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.Type == TransactionType.Expense &&
                    t.Category != null &&
                    t.TransactionDate >= monthStart &&
                    t.TransactionDate <= monthEnd)
                .GroupBy(t => new
                {
                    t.CategoryId,
                    CategoryName = t.Category!.Name
                })
                .Select(g => new
                {
                    g.Key.CategoryName,
                    TotalAmount = g.Sum(t => t.Amount)
                })
                .OrderByDescending(x => x.TotalAmount)
                .ThenBy(x => x.CategoryName)
                .FirstOrDefaultAsync();

            if (topCategory == null)
            {
                return;
            }

            var share = Math.Round((topCategory.TotalAmount / totalMonthlyExpense) * 100m, 2);
            insights.Add(new FinancialInsightDto
            {
                Title = "Top Spending Category",
                Description = $"{topCategory.CategoryName} accounts for {share:0.##}% of your expenses this month.",
                Severity = FinancialInsightSeverity.Info,
                CreatedAt = createdAt
            });
        }

        private async Task AddMonthlySpendingChangeInsightAsync(
            ICollection<FinancialInsightDto> insights,
            string userId,
            DateTime monthStart,
            DateTime monthEnd,
            DateTime previousMonthStart,
            DateTime previousMonthEnd,
            DateTime createdAt)
        {
            var currentMonthExpense = await _context.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.Type == TransactionType.Expense &&
                    t.TransactionDate >= monthStart &&
                    t.TransactionDate <= monthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            var previousMonthExpense = await _context.Transactions
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.Type == TransactionType.Expense &&
                    t.TransactionDate >= previousMonthStart &&
                    t.TransactionDate <= previousMonthEnd)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            if (currentMonthExpense <= 0m && previousMonthExpense <= 0m)
            {
                return;
            }

            if (previousMonthExpense <= 0m && currentMonthExpense > 0m)
            {
                insights.Add(new FinancialInsightDto
                {
                    Title = "Spending Increased",
                    Description = $"You spent PHP {currentMonthExpense:N2} this month after no spending last month.",
                    Severity = FinancialInsightSeverity.Warning,
                    CreatedAt = createdAt
                });
                return;
            }

            if (previousMonthExpense > 0m && currentMonthExpense <= 0m)
            {
                insights.Add(new FinancialInsightDto
                {
                    Title = "Spending Decreased",
                    Description = "Your spending dropped to PHP 0.00 compared to last month.",
                    Severity = FinancialInsightSeverity.Info,
                    CreatedAt = createdAt
                });
                return;
            }

            var changePercentage = Math.Round(((currentMonthExpense - previousMonthExpense) / previousMonthExpense) * 100m, 2);

            if (changePercentage > 0m)
            {
                insights.Add(new FinancialInsightDto
                {
                    Title = "Spending Increased",
                    Description = $"Your spending increased by {changePercentage:0.##}% compared to last month.",
                    Severity = changePercentage >= 20m
                        ? FinancialInsightSeverity.Warning
                        : FinancialInsightSeverity.Info,
                    CreatedAt = createdAt
                });
            }
            else if (changePercentage < 0m)
            {
                insights.Add(new FinancialInsightDto
                {
                    Title = "Spending Decreased",
                    Description = $"Your spending decreased by {Math.Abs(changePercentage):0.##}% compared to last month.",
                    Severity = FinancialInsightSeverity.Info,
                    CreatedAt = createdAt
                });
            }
        }

        private async Task AddBudgetRiskInsightsAsync(
            ICollection<FinancialInsightDto> insights,
            string userId,
            DateTime nowUtc)
        {
            var activeBudgetUsage = await _context.Budgets
                .AsNoTracking()
                .Where(b =>
                    b.UserId == userId &&
                    b.BudgetPeriod != null &&
                    b.BudgetPeriod.StartDate <= nowUtc &&
                    b.BudgetPeriod.EndDate >= nowUtc)
                .Select(b => new
                {
                    CategoryName = b.Category != null ? b.Category.Name : "Uncategorized",
                    BudgetAmount = b.PlannedAmount,
                    ActualSpent = _context.Transactions
                        .Where(t =>
                            t.UserId == userId &&
                            t.Type == TransactionType.Expense &&
                            t.CategoryId == b.CategoryId &&
                            t.TransactionDate >= b.BudgetPeriod!.StartDate &&
                            t.TransactionDate <= b.BudgetPeriod!.EndDate &&
                            t.TransactionDate <= nowUtc)
                        .Sum(t => (decimal?)t.Amount) ?? 0m
                })
                .ToListAsync();

            var riskCandidates = activeBudgetUsage
                .Where(x => x.BudgetAmount > 0m)
                .Select(x => new
                {
                    x.CategoryName,
                    x.BudgetAmount,
                    x.ActualSpent,
                    UsagePercent = Math.Round((x.ActualSpent / x.BudgetAmount) * 100m, 2)
                })
                .Where(x => x.UsagePercent >= 90m)
                .OrderByDescending(x => x.UsagePercent)
                .ThenBy(x => x.CategoryName)
                .Take(3)
                .ToList();

            foreach (var risk in riskCandidates)
            {
                if (risk.UsagePercent > 100m)
                {
                    insights.Add(new FinancialInsightDto
                    {
                        Title = "Budget Exceeded",
                        Description = $"You exceeded your {risk.CategoryName} budget by PHP {(risk.ActualSpent - risk.BudgetAmount):N2}.",
                        Severity = FinancialInsightSeverity.Alert,
                        CreatedAt = nowUtc
                    });
                }
                else
                {
                    insights.Add(new FinancialInsightDto
                    {
                        Title = "Budget Warning",
                        Description = $"You have used {risk.UsagePercent:0.##}% of your {risk.CategoryName} budget.",
                        Severity = FinancialInsightSeverity.Warning,
                        CreatedAt = nowUtc
                    });
                }
            }
        }

        private async Task AddSubscriptionInsightAsync(
            ICollection<FinancialInsightDto> insights,
            string userId,
            DateTime createdAt)
        {
            var recurringSubscriptions = await _context.RecurringTransactions
                .AsNoTracking()
                .Where(rt =>
                    rt.UserId == userId &&
                    rt.IsActive &&
                    rt.Type == TransactionType.Expense)
                .Select(rt => new
                {
                    rt.Amount,
                    rt.Frequency
                })
                .ToListAsync();

            if (recurringSubscriptions.Count == 0)
            {
                return;
            }

            var monthlySubscriptionTotal = recurringSubscriptions
                .Sum(x => ConvertToMonthlyAmount(x.Amount, x.Frequency));

            if (monthlySubscriptionTotal <= 0m)
            {
                return;
            }

            insights.Add(new FinancialInsightDto
            {
                Title = "Subscriptions",
                Description = $"Your recurring subscriptions total PHP {monthlySubscriptionTotal:N2} per month.",
                Severity = FinancialInsightSeverity.Info,
                CreatedAt = createdAt
            });
        }

        private static decimal ConvertToMonthlyAmount(decimal amount, RecurringFrequency frequency)
        {
            var monthlyAmount = frequency switch
            {
                RecurringFrequency.Daily => amount * 30m,
                RecurringFrequency.Weekly => amount * (52m / 12m),
                RecurringFrequency.Monthly => amount,
                RecurringFrequency.Yearly => amount / 12m,
                _ => 0m
            };

            return Math.Round(monthlyAmount, 2);
        }
    }
}
