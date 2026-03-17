using Microsoft.EntityFrameworkCore;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests;

public class InMemoryDbContextInfrastructureTests
{
    [Fact]
    public void Create_UsesInMemoryProvider()
    {
        using var context = TestDbContextFactory.Create();

        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", context.Database.ProviderName);
    }

    [Fact]
    public async Task Create_UsesIsolatedDatabasePerCall()
    {
        await using var firstContext = TestDbContextFactory.Create();
        TestDataSeeder.SeedTransactions(firstContext);
        var firstCount = await firstContext.Transactions.CountAsync();

        await using var secondContext = TestDbContextFactory.Create();
        var secondCount = await secondContext.Transactions.CountAsync();

        Assert.Equal(2, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task SeedTransactions_CreatesDeterministicDataset()
    {
        await using var context = TestDbContextFactory.Create();
        var seeded = TestDataSeeder.SeedTransactions(context);

        var rows = await context.Transactions
            .AsNoTracking()
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, seeded.Count);
        Assert.Equal("Test transaction 1", rows[0].Description);
        Assert.Equal("Test transaction 2", rows[1].Description);
        Assert.Equal(142.50m, rows.Sum(t => t.Amount));
    }
}
