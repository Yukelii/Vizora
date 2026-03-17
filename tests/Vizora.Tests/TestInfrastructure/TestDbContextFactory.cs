using Microsoft.EntityFrameworkCore;
using Vizora.Data;

namespace Vizora.Tests.TestInfrastructure;

public static class TestDbContextFactory
{
    public static ApplicationDbContext Create(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .EnableSensitiveDataLogging()
            .Options;

        return new ApplicationDbContext(options);
    }
}
