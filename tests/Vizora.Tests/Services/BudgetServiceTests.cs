using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class BudgetServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task CreateAsync_SavesBudgetWithExpectedPeriodAndCategory()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Housing", TransactionType.Expense);
        var service = CreateService(context);

        await service.CreateAsync(new BudgetUpsertRequest
        {
            CategoryId = category.Id,
            PlannedAmount = 350m,
            PeriodType = BudgetPeriodType.Monthly,
            StartDate = new DateTime(2026, 2, 1),
            EndDate = new DateTime(2026, 2, 28)
        });

        var saved = await context.Budgets
            .AsNoTracking()
            .Include(b => b.BudgetPeriod)
            .SingleAsync();

        Assert.Equal(TestDataSeeder.DefaultUserId, saved.UserId);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.Equal(350m, saved.PlannedAmount);
        Assert.NotNull(saved.BudgetPeriod);
        Assert.Equal(BudgetPeriodType.Monthly, saved.BudgetPeriod!.Type);
        Assert.Equal(new DateTime(2026, 2, 1), saved.BudgetPeriod.StartDate.Date);
        Assert.Equal(new DateTime(2026, 2, 28), saved.BudgetPeriod.EndDate.Date);
        Assert.Equal(DateTimeKind.Utc, saved.BudgetPeriod.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, saved.BudgetPeriod.EndDate.Kind);
    }

    [Fact]
    public async Task CreateAsync_WhenDuplicateBudgetExists_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Savings Goal", TransactionType.Expense);
        var service = CreateService(context);
        var request = new BudgetUpsertRequest
        {
            CategoryId = category.Id,
            PlannedAmount = 100m,
            PeriodType = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 31)
        };

        await service.CreateAsync(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.Budgets.CountAsync());
    }

    [Fact]
    public async Task GetPerformanceByIdAsync_FlagsOverBudgetWhenSpendingExceedsPlan()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Dining", TransactionType.Expense);
        var service = CreateService(context);

        await service.CreateAsync(new BudgetUpsertRequest
        {
            CategoryId = category.Id,
            PlannedAmount = 100m,
            PeriodType = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 31)
        });

        var budgetId = await context.Budgets.Select(b => b.Id).SingleAsync();
        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 70m,
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
                Description = "Salary",
                TransactionDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 30m,
                Description = "Outside range",
                TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var performance = await service.GetPerformanceByIdAsync(budgetId);

        Assert.NotNull(performance);
        Assert.Equal(100m, performance!.PlannedAmount);
        Assert.Equal(120m, performance.ActualSpending);
        Assert.Equal(-20m, performance.RemainingAmount);
        Assert.Equal(120m, performance.UsagePercent);
        Assert.True(performance.IsOverBudget);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotAllowCrossUserDeletion()
    {
        await using var context = TestDbContextFactory.Create();
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other User Expense", TransactionType.Expense);
        var otherUserBudgetService = CreateService(context, OtherUserId);

        await otherUserBudgetService.CreateAsync(new BudgetUpsertRequest
        {
            CategoryId = otherCategory.Id,
            PlannedAmount = 200m,
            PeriodType = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31)
        });

        var otherBudgetId = await context.Budgets
            .Where(b => b.UserId == OtherUserId)
            .Select(b => b.Id)
            .SingleAsync();

        var service = CreateService(context, TestDataSeeder.DefaultUserId);
        var deleted = await service.DeleteAsync(otherBudgetId);

        Assert.False(deleted);
        Assert.Equal(1, await context.Budgets.CountAsync());
    }

    private static BudgetService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new BudgetService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<BudgetService>.Instance);
    }
}
