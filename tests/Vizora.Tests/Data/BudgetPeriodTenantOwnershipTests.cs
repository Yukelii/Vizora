using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vizora.Data;
using Vizora.Models;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Data;

public class BudgetPeriodTenantOwnershipTests
{
    [Fact]
    public void ModelConfig_BudgetToBudgetPeriodForeignKey_IsTenantCoupled()
    {
        using var context = TestDbContextFactory.Create();
        var budgetEntity = context.Model.FindEntityType(typeof(Budget));
        Assert.NotNull(budgetEntity);

        var fk = budgetEntity!
            .GetForeignKeys()
            .Single(relationship => relationship.PrincipalEntityType.ClrType == typeof(BudgetPeriod));

        var dependentProperties = fk.Properties.Select(property => property.Name).ToArray();
        var principalProperties = fk.PrincipalKey.Properties.Select(property => property.Name).ToArray();

        Assert.Equal(new[] { "BudgetPeriodId", "UserId" }, dependentProperties);
        Assert.Equal(new[] { "Id", "UserId" }, principalProperties);
    }

    [Fact]
    public async Task SaveChanges_WithSameUserBudgetAndPeriod_Succeeds()
    {
        await using var context = await CreateSqliteContextAsync();
        var user = TestDataSeeder.EnsureUser(context, "tenant-a");
        var category = TestDataSeeder.EnsureCategory(context, user.Id, "Food", TransactionType.Expense);

        var period = new BudgetPeriod
        {
            UserId = user.Id,
            Type = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        context.BudgetPeriods.Add(period);
        await context.SaveChangesAsync();

        context.Budgets.Add(new Budget
        {
            UserId = user.Id,
            CategoryId = category.Id,
            BudgetPeriodId = period.Id,
            PlannedAmount = 100m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        Assert.Equal(1, await context.Budgets.CountAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenBudgetReferencesOtherUsersPeriod_Throws()
    {
        await using var context = await CreateSqliteContextAsync();
        var userA = TestDataSeeder.EnsureUser(context, "tenant-a");
        var userB = TestDataSeeder.EnsureUser(context, "tenant-b");
        var categoryA = TestDataSeeder.EnsureCategory(context, userA.Id, "Food", TransactionType.Expense);

        var userBPeriod = new BudgetPeriod
        {
            UserId = userB.Id,
            Type = BudgetPeriodType.Custom,
            StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        context.BudgetPeriods.Add(userBPeriod);
        await context.SaveChangesAsync();

        context.Budgets.Add(new Budget
        {
            UserId = userA.Id,
            CategoryId = categoryA.Id,
            BudgetPeriodId = userBPeriod.Id,
            PlannedAmount = 80m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static async Task<ApplicationDbContext> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
