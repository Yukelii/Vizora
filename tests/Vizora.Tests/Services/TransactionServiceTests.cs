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
    public async Task UpdateAsync_WithValidRowVersionAndChangedValues_Succeeds()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var transaction = new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 25m,
            Description = "Initial",
            TransactionDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var originalRowVersion = transaction.RowVersion.ToArray();
        var service = CreateService(context);

        var updated = await service.UpdateAsync(new Transaction
        {
            Id = transaction.Id,
            RowVersion = originalRowVersion,
            CategoryId = category.Id,
            Amount = 42.75m,
            Description = "Updated",
            TransactionDate = transaction.TransactionDate
        });

        var reloaded = await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == transaction.Id);

        Assert.Equal(UpdateOperationStatus.Success, updated.Status);
        Assert.Equal(42.75m, reloaded.Amount);
        Assert.Equal("Updated", reloaded.Description);
        Assert.NotEmpty(reloaded.RowVersion);
        Assert.False(originalRowVersion.AsSpan().SequenceEqual(reloaded.RowVersion));
    }

    [Fact]
    public async Task UpdateAsync_WithMissingRowVersion_ReturnsConflictWithReloadGuidance()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var transaction = new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 25m,
            Description = "Initial",
            TransactionDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var persisted = await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        var service = CreateService(context);

        var updated = await service.UpdateAsync(new Transaction
        {
            Id = transaction.Id,
            RowVersion = Array.Empty<byte>(),
            CategoryId = category.Id,
            Amount = 42.75m,
            Description = "Updated",
            TransactionDate = transaction.TransactionDate
        });

        Assert.Equal(UpdateOperationStatus.Conflict, updated.Status);
        Assert.NotNull(updated.Conflict);
        Assert.Contains("out of sync", updated.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Convert.ToHexString(persisted.RowVersion), updated.Conflict!.DatabaseValues.RowVersionHex);
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

    [Fact]
    public async Task GetPagedAsync_ReturnsCategoryVisualDataForLinkedCategory()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Travel", TransactionType.Expense);
        category.IconKey = "flight";
        category.ColorKey = "indigo";
        context.Categories.Update(category);

        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 580m,
            Description = "Flight ticket",
            TransactionDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetPagedAsync(new TransactionListQuery { Page = 1, PageSize = 20 });

        var row = Assert.Single(result.Items);
        Assert.NotNull(row.Category);
        Assert.Equal("flight", row.Category!.IconKey);
        Assert.Equal("indigo", row.Category.ColorKey);
    }

    [Fact]
    public async Task GetPagedAsync_AfterCategoryVisualUpdate_ReturnsLatestCategoryVisuals()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Leisure", TransactionType.Expense);
        category.IconKey = "movie";
        category.ColorKey = "purple";
        context.Categories.Update(category);

        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 320m,
            Description = "Weekend plan",
            TransactionDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        category.IconKey = "pets";
        category.ColorKey = "rose";
        context.Categories.Update(category);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetPagedAsync(new TransactionListQuery { Page = 1, PageSize = 20 });

        var row = Assert.Single(result.Items);
        Assert.NotNull(row.Category);
        Assert.Equal("pets", row.Category!.IconKey);
        Assert.Equal("rose", row.Category.ColorKey);
    }

    [Fact]
    public async Task GetPagedAsync_OnlyReturnsCurrentUserCategoryVisuals()
    {
        await using var context = TestDbContextFactory.Create();
        var defaultCategory = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        defaultCategory.IconKey = "restaurant";
        defaultCategory.ColorKey = "emerald";
        context.Categories.Update(defaultCategory);

        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Travel", TransactionType.Expense);
        otherCategory.IconKey = "flight";
        otherCategory.ColorKey = "red";
        context.Categories.Update(otherCategory);

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = defaultCategory.Id,
                Type = TransactionType.Expense,
                Amount = 75m,
                Description = "Dinner",
                TransactionDate = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                Type = TransactionType.Expense,
                Amount = 150m,
                Description = "Other tenant row",
                TransactionDate = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context, TestDataSeeder.DefaultUserId);
        var result = await service.GetPagedAsync(new TransactionListQuery { Page = 1, PageSize = 20 });

        var row = Assert.Single(result.Items);
        Assert.Equal(TestDataSeeder.DefaultUserId, row.UserId);
        Assert.NotNull(row.Category);
        Assert.Equal("restaurant", row.Category!.IconKey);
        Assert.Equal("emerald", row.Category.ColorKey);
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
