using Microsoft.EntityFrameworkCore;
using Vizora.Data;
using Vizora.Enums;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class FinancialInsightsServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task GetInsightsAsync_GeneratesInsightsForCommonFinancePatterns()
    {
        await using var context = TestDbContextFactory.Create();
        var nowUtc = DateTime.UtcNow;
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
        var previousMonthStart = monthStart.AddMonths(-1);

        var food = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var utilities = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Utilities", TransactionType.Expense);
        var other = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                Type = TransactionType.Expense,
                Amount = 200m,
                Description = "Food current",
                TransactionDate = monthStart.AddDays(3),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = utilities.Id,
                Type = TransactionType.Expense,
                Amount = 100m,
                Description = "Utilities current",
                TransactionDate = monthStart.AddDays(4),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                Type = TransactionType.Expense,
                Amount = 100m,
                Description = "Food previous",
                TransactionDate = previousMonthStart.AddDays(5),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = other.Id,
                Type = TransactionType.Expense,
                Amount = 5000m,
                Description = "Other user data",
                TransactionDate = monthStart.AddDays(2),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        var currentPeriod = new BudgetPeriod
        {
            UserId = TestDataSeeder.DefaultUserId,
            Type = BudgetPeriodType.Monthly,
            StartDate = monthStart,
            EndDate = monthEnd,
            CreatedAt = DateTime.UtcNow
        };
        var otherPeriod = new BudgetPeriod
        {
            UserId = OtherUserId,
            Type = BudgetPeriodType.Monthly,
            StartDate = monthStart,
            EndDate = monthEnd,
            CreatedAt = DateTime.UtcNow
        };
        context.BudgetPeriods.AddRange(currentPeriod, otherPeriod);
        await context.SaveChangesAsync();

        context.Budgets.AddRange(
            new Budget
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = food.Id,
                BudgetPeriodId = currentPeriod.Id,
                PlannedAmount = 150m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Budget
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = utilities.Id,
                BudgetPeriodId = currentPeriod.Id,
                PlannedAmount = 110m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Budget
            {
                UserId = OtherUserId,
                CategoryId = other.Id,
                BudgetPeriodId = otherPeriod.Id,
                PlannedAmount = 10m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        context.RecurringTransactions.AddRange(
            new RecurringTransaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = utilities.Id,
                Type = TransactionType.Expense,
                Amount = 30m,
                Description = "Streaming",
                Frequency = RecurringFrequency.Monthly,
                StartDate = monthStart,
                NextRunDate = monthStart,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new RecurringTransaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = utilities.Id,
                Type = TransactionType.Expense,
                Amount = 12m,
                Description = "Weekly plan",
                Frequency = RecurringFrequency.Weekly,
                StartDate = monthStart,
                NextRunDate = monthStart,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new RecurringTransaction
            {
                UserId = OtherUserId,
                CategoryId = other.Id,
                Type = TransactionType.Expense,
                Amount = 999m,
                Description = "Other recurring",
                Frequency = RecurringFrequency.Monthly,
                StartDate = monthStart,
                NextRunDate = monthStart,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var insights = await service.GetInsightsAsync();

        Assert.NotEmpty(insights);
        Assert.Contains(insights, insight =>
            insight.Title == "Top Spending Category" &&
            insight.Description.Contains("Food", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(insights, insight =>
            insight.Title == "Spending Increased" &&
            insight.Description.Contains("%", StringComparison.Ordinal));
        Assert.Contains(insights, insight => insight.Title == "Budget Exceeded");
        Assert.Contains(insights, insight => insight.Title == "Budget Warning");
        Assert.Contains(insights, insight =>
            insight.Title == "Subscriptions" &&
            insight.Description.Contains("PHP 82.00", StringComparison.Ordinal));
        Assert.DoesNotContain(insights, insight =>
            insight.Description.Contains("Other", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInsightsAsync_WhenNoData_ReturnsEmpty()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);

        var insights = await service.GetInsightsAsync();

        Assert.Empty(insights);
    }

    [Fact]
    public async Task GetInsightsAsync_WhenOnlyOtherUserHasData_ReturnsEmptyForCurrentUser()
    {
        await using var context = TestDbContextFactory.Create();
        var nowUtc = DateTime.UtcNow;
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);
        context.Transactions.Add(new Transaction
        {
            UserId = OtherUserId,
            CategoryId = otherCategory.Id,
            Type = TransactionType.Expense,
            Amount = 200m,
            Description = "Other user only",
            TransactionDate = monthStart.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var insights = await service.GetInsightsAsync();

        Assert.Empty(insights);
    }

    private static FinancialInsightsService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new FinancialInsightsService(context, new TestUserContextService(userId));
    }
}
