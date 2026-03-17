using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Enums;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class RecurringTransactionServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task GenerateDueTransactionsAsync_CreatesTransactionsForDueEntries()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Subscription", TransactionType.Expense);
        context.RecurringTransactions.Add(new RecurringTransaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 19.99m,
            Description = "Music",
            Frequency = RecurringFrequency.Weekly,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NextRunDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var runUntil = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var generated = await service.GenerateDueTransactionsAsync(runUntil);

        var transactions = await context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == TestDataSeeder.DefaultUserId)
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();

        var recurring = await context.RecurringTransactions.AsNoTracking().SingleAsync();

        Assert.Equal(3, generated);
        Assert.Equal(3, transactions.Count);
        Assert.Equal(new DateTime(2026, 1, 1), transactions[0].TransactionDate.Date);
        Assert.Equal(new DateTime(2026, 1, 8), transactions[1].TransactionDate.Date);
        Assert.Equal(new DateTime(2026, 1, 15), transactions[2].TransactionDate.Date);
        Assert.Equal(new DateTime(2026, 1, 22), recurring.NextRunDate.Date);
    }

    [Fact]
    public async Task GenerateDueTransactionsAsync_IsIdempotentForSameWindow()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Gym", TransactionType.Expense);
        context.RecurringTransactions.Add(new RecurringTransaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 50m,
            Description = "Membership",
            Frequency = RecurringFrequency.Weekly,
            StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            NextRunDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var runUntil = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        var first = await service.GenerateDueTransactionsAsync(runUntil);
        var second = await service.GenerateDueTransactionsAsync(runUntil);

        Assert.Equal(3, first);
        Assert.Equal(0, second);
        Assert.Equal(3, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task GenerateDueTransactionsAsync_SkipsDisabledRecurringEntries()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Phone", TransactionType.Expense);
        context.RecurringTransactions.Add(new RecurringTransaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 40m,
            Description = "Plan",
            Frequency = RecurringFrequency.Monthly,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NextRunDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var generated = await service.GenerateDueTransactionsAsync(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, generated);
        Assert.Equal(0, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenCategoryBelongsToAnotherUser_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var otherUserCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other Category", TransactionType.Expense);
        var service = CreateService(context, TestDataSeeder.DefaultUserId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new RecurringTransaction
            {
                CategoryId = otherUserCategory.Id,
                Amount = 25m,
                Description = "Cross-user recurring",
                Frequency = RecurringFrequency.Monthly,
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                NextRunDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            }));

        Assert.Contains("category", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await context.RecurringTransactions.CountAsync());
    }

    private static RecurringTransactionService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new RecurringTransactionService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<RecurringTransactionService>.Instance);
    }
}
