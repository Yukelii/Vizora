using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class CategoryServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task CreateAsync_SetsOwnershipAndCreatedTimestamp()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await service.CreateAsync(new Category
        {
            Name = "  Food  ",
            Type = TransactionType.Expense
        });

        var after = DateTime.UtcNow.AddSeconds(1);
        var saved = await context.Categories.AsNoTracking().SingleAsync();

        Assert.Equal(TestDataSeeder.DefaultUserId, saved.UserId);
        Assert.Equal("Food", saved.Name);
        Assert.InRange(saved.CreatedAt, before, after);
        Assert.NotEmpty(saved.RowVersion);
    }

    [Fact]
    public async Task CreateAsync_PreventsCaseInsensitiveDuplicateNames()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food");
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Category
            {
                Name = "  food  ",
                Type = TransactionType.Expense
            }));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WhenCategoryHasTransactions_Throws()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Transport");
        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = category.Type,
            Amount = 42m,
            Description = "Transit",
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(category.Id));

        Assert.Contains("used by existing transactions", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowCrossUserMutation()
    {
        await using var context = TestDbContextFactory.Create();
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Utilities");
        var service = CreateService(context, TestDataSeeder.DefaultUserId);

        var updated = await service.UpdateAsync(new Category
        {
            Id = otherCategory.Id,
            RowVersion = otherCategory.RowVersion,
            Name = "Changed Name",
            Type = otherCategory.Type
        });

        var reloaded = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == otherCategory.Id);

        Assert.Equal(UpdateOperationStatus.NotFound, updated.Status);
        Assert.Equal("Utilities", reloaded.Name);
        Assert.Equal(OtherUserId, reloaded.UserId);
    }

    [Fact]
    public async Task UpdateAsync_WithValidRowVersionAndChangedValues_Succeeds()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var originalRowVersion = category.RowVersion.ToArray();
        var service = CreateService(context);

        var updated = await service.UpdateAsync(new Category
        {
            Id = category.Id,
            RowVersion = originalRowVersion,
            Name = "Dining Out",
            Type = TransactionType.Expense,
            IconKey = category.IconKey,
            ColorKey = category.ColorKey
        });

        var reloaded = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);

        Assert.Equal(UpdateOperationStatus.Success, updated.Status);
        Assert.Equal("Dining Out", reloaded.Name);
        Assert.NotEmpty(reloaded.RowVersion);
        Assert.False(originalRowVersion.AsSpan().SequenceEqual(reloaded.RowVersion));
    }

    [Fact]
    public async Task UpdateAsync_WithMissingRowVersion_ReturnsConflictWithReloadGuidance()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var persisted = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        var service = CreateService(context);

        var updated = await service.UpdateAsync(new Category
        {
            Id = category.Id,
            RowVersion = Array.Empty<byte>(),
            Name = "Dining Out",
            Type = TransactionType.Expense,
            IconKey = persisted.IconKey,
            ColorKey = persisted.ColorKey
        });

        Assert.Equal(UpdateOperationStatus.Conflict, updated.Status);
        Assert.NotNull(updated.Conflict);
        Assert.Contains("out of sync", updated.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Convert.ToHexString(persisted.RowVersion), updated.Conflict!.DatabaseValues.RowVersionHex);
    }

    private static CategoryService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new CategoryService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<CategoryService>.Instance);
    }
}
