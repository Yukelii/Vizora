using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class TransactionServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task CreateAsync_SavesTransactionForCurrentUserAndCategory()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food");
        var service = CreateService(context);
        var transactionDate = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync(new Transaction
        {
            CategoryId = category.Id,
            Amount = 25.50m,
            Description = " Lunch ",
            TransactionDate = transactionDate
        });

        var saved = await context.Transactions.AsNoTracking().SingleAsync();

        Assert.Equal(TestDataSeeder.DefaultUserId, saved.UserId);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.Equal(TransactionType.Expense, saved.Type);
        Assert.Equal(25.50m, saved.Amount);
        Assert.Equal("Lunch", saved.Description);
        Assert.Equal(transactionDate, saved.TransactionDate);
    }

    [Fact]
    public async Task CreateAsync_WhenCategoryBelongsToAnotherUser_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var otherUserCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Rent");
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Transaction
            {
                CategoryId = otherUserCategory.Id,
                Amount = 20m,
                Description = "Cross-user create",
                TransactionDate = DateTime.UtcNow
            }));

        Assert.Contains("category", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenAmountIsZero_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context);
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Transaction
            {
                CategoryId = category.Id,
                Amount = 0m,
                Description = "Zero amount",
                TransactionDate = DateTime.UtcNow
            }));

        Assert.Contains("greater than 0", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenAmountIsNegative_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context);
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Transaction
            {
                CategoryId = category.Id,
                Amount = -4.99m,
                Description = "Negative amount",
                TransactionDate = DateTime.UtcNow
            }));

        Assert.Contains("greater than 0", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotReturnOtherUsersTransaction()
    {
        await using var context = TestDbContextFactory.Create();
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Utilities");
        var transaction = new Transaction
        {
            UserId = OtherUserId,
            CategoryId = otherCategory.Id,
            Type = TransactionType.Expense,
            Amount = 99m,
            Description = "Other user transaction",
            TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var service = CreateService(context, TestDataSeeder.DefaultUserId);

        var found = await service.GetByIdAsync(transaction.Id);
        var visibleTransactions = await service.GetAllAsync();

        Assert.Null(found);
        Assert.Empty(visibleTransactions);
    }

    [Fact]
    public async Task CreateAsync_WhenAuditLoggingFails_StillPersistsTransaction()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context);
        var service = new TransactionService(
            context,
            new TestUserContextService(TestDataSeeder.DefaultUserId),
            new ThrowingAuditService(),
            NullLogger<TransactionService>.Instance);

        await service.CreateAsync(new Transaction
        {
            CategoryId = category.Id,
            Amount = 25m,
            Description = "Audit failure tolerance",
            TransactionDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var saved = await context.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(TestDataSeeder.DefaultUserId, saved.UserId);
        Assert.Equal(25m, saved.Amount);
    }

    private static TransactionService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new TransactionService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<TransactionService>.Instance);
    }
}
