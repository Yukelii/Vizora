using Microsoft.EntityFrameworkCore;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class FinanceAnalyticsServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task GetDashboardStatisticsAsync_ComputesTotalsMonthlyRollupsAndTopCategories()
    {
        await using var context = TestDbContextFactory.Create();
        var salary = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Salary", TransactionType.Income);
        var food = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var rent = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Rent", TransactionType.Expense);
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = salary.Id,
                Type = TransactionType.Income,
                Amount = 1000m,
                Description = "Payroll",
                TransactionDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                Type = TransactionType.Expense,
                Amount = 100m,
                Description = "Food Jan",
                TransactionDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = rent.Id,
                Type = TransactionType.Expense,
                Amount = 400m,
                Description = "Rent Feb",
                TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                Type = TransactionType.Expense,
                Amount = 50m,
                Description = "Food Feb",
                TransactionDate = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                Type = TransactionType.Expense,
                Amount = 5000m,
                Description = "Other user",
                TransactionDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var dashboard = await service.GetDashboardStatisticsAsync(
            filter: "all",
            startDate: new DateTime(2026, 1, 1),
            endDate: new DateTime(2026, 2, 28));

        Assert.Equal(1000m, dashboard.TotalIncome);
        Assert.Equal(550m, dashboard.TotalExpense);
        Assert.Equal(450m, dashboard.NetCashFlow);
        Assert.Equal(4, dashboard.TotalTransactions);
        Assert.Equal(2, dashboard.TopSpendingCategories.Count);
        Assert.Equal("Rent", dashboard.TopSpendingCategories[0].CategoryName);
        Assert.Equal(400m, dashboard.TopSpendingCategories[0].TotalAmount);
        Assert.Equal(2, dashboard.MonthlyLabels.Count);
        Assert.Equal("2026-01", dashboard.MonthlyLabels[0]);
        Assert.Equal("2026-02", dashboard.MonthlyLabels[1]);
        Assert.Equal(1000m, dashboard.MonthlyIncomeData[0]);
        Assert.Equal(0m, dashboard.MonthlyIncomeData[1]);
        Assert.Equal(100m, dashboard.MonthlyExpenseData[0]);
        Assert.Equal(450m, dashboard.MonthlyExpenseData[1]);
    }

    [Fact]
    public async Task GetCategorySpendingAsync_RespectsLimitAndUserIsolation()
    {
        await using var context = TestDbContextFactory.Create();
        var food = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var rent = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Rent", TransactionType.Expense);
        var salary = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Salary", TransactionType.Income);
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                Type = TransactionType.Expense,
                Amount = 120m,
                Description = "Food",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = rent.Id,
                Type = TransactionType.Expense,
                Amount = 250m,
                Description = "Rent",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = salary.Id,
                Type = TransactionType.Income,
                Amount = 1000m,
                Description = "Salary",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                Type = TransactionType.Expense,
                Amount = 9999m,
                Description = "Other user",
                TransactionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var spending = await service.GetCategorySpendingAsync(limit: 1);

        Assert.Single(spending);
        Assert.Equal("Rent", spending[0].CategoryName);
        Assert.Equal(250m, spending[0].TotalAmount);
    }

    [Fact]
    public async Task GetBudgetProgressAsync_ComputesUsageForCurrentUserBudgetOnly()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);

        var period = new BudgetPeriod
        {
            UserId = TestDataSeeder.DefaultUserId,
            Type = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var otherPeriod = new BudgetPeriod
        {
            UserId = OtherUserId,
            Type = BudgetPeriodType.Custom,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            CreatedAt = DateTime.UtcNow
        };

        context.BudgetPeriods.AddRange(period, otherPeriod);
        await context.SaveChangesAsync();

        context.Budgets.AddRange(
            new Budget
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                BudgetPeriodId = period.Id,
                PlannedAmount = 100m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Budget
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                BudgetPeriodId = otherPeriod.Id,
                PlannedAmount = 100m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 60m,
                Description = "Week 1",
                TransactionDate = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 50m,
                Description = "Week 2",
                TransactionDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Income,
                Amount = 500m,
                Description = "Income",
                TransactionDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                Type = TransactionType.Expense,
                Amount = 999m,
                Description = "Other user",
                TransactionDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var progress = await service.GetBudgetProgressAsync();

        Assert.Single(progress);
        Assert.Equal(category.Id, progress[0].CategoryId);
        Assert.Equal(110m, progress[0].ActualSpent);
        Assert.Equal(-10m, progress[0].RemainingAmount);
        Assert.Equal(110m, progress[0].PercentageUsed);
    }

    [Fact]
    public async Task GetDashboardStatisticsAsync_WhenNoTransactions_ReturnsZeroedState()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);

        var dashboard = await service.GetDashboardStatisticsAsync();

        Assert.Equal(0m, dashboard.TotalIncome);
        Assert.Equal(0m, dashboard.TotalExpense);
        Assert.Equal(0m, dashboard.NetCashFlow);
        Assert.Equal(0, dashboard.TotalTransactions);
        Assert.Empty(dashboard.MonthlyLabels);
        Assert.Empty(dashboard.TopSpendingCategories);
        Assert.Empty(dashboard.RecentTransactions);
    }

    private static FinanceAnalyticsService CreateService(
        ApplicationDbContext context,
        string userId = TestDataSeeder.DefaultUserId,
        IReadOnlyList<BudgetPerformanceViewModel>? budgetPerformance = null,
        IReadOnlyList<FinancialInsightDto>? insights = null)
    {
        return new FinanceAnalyticsService(
            context,
            new TestUserContextService(userId),
            new StubBudgetService(budgetPerformance ?? Array.Empty<BudgetPerformanceViewModel>()),
            new StubFinancialInsightsService(insights ?? Array.Empty<FinancialInsightDto>()));
    }

    private sealed class StubBudgetService : IBudgetService
    {
        private readonly IReadOnlyList<BudgetPerformanceViewModel> _budgets;

        public StubBudgetService(IReadOnlyList<BudgetPerformanceViewModel> budgets)
        {
            _budgets = budgets;
        }

        public Task<IReadOnlyList<BudgetPerformanceViewModel>> GetAllWithPerformanceAsync(DateTime? filterStartDate = null, DateTime? filterEndDate = null)
        {
            return Task.FromResult(_budgets);
        }

        public Task<BudgetPerformanceViewModel?> GetPerformanceByIdAsync(int id)
        {
            return Task.FromResult<BudgetPerformanceViewModel?>(_budgets.FirstOrDefault(b => b.BudgetId == id));
        }

        public Task<Budget?> GetByIdAsync(int id)
        {
            return Task.FromResult<Budget?>(null);
        }

        public Task CreateAsync(BudgetUpsertRequest request)
        {
            throw new NotSupportedException("Not used in analytics tests.");
        }

        public Task<UpdateOperationResult<BudgetConflictSnapshot>> UpdateAsync(int id, BudgetUpsertRequest request, bool forceOverwrite = false)
        {
            throw new NotSupportedException("Not used in analytics tests.");
        }

        public Task<bool> DeleteAsync(int id)
        {
            throw new NotSupportedException("Not used in analytics tests.");
        }
    }

    private sealed class StubFinancialInsightsService : IFinancialInsightsService
    {
        private readonly IReadOnlyList<FinancialInsightDto> _insights;

        public StubFinancialInsightsService(IReadOnlyList<FinancialInsightDto> insights)
        {
            _insights = insights;
        }

        public Task<IReadOnlyList<FinancialInsightDto>> GetInsightsAsync()
        {
            return Task.FromResult(_insights);
        }
    }
}
