using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class AuditServiceTests
{
    [Fact]
    public async Task LogAsync_PersistsAuditEntryWithUserAndPayload()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureUser(context, TestDataSeeder.DefaultUserId);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var service = new AuditService(
            context,
            new TestUserContextService(TestDataSeeder.DefaultUserId),
            httpContextAccessor,
            NullLogger<AuditService>.Instance);

        await service.LogAsync(new AuditLogRequest
        {
            EventType = "create",
            EntityType = "Transaction",
            EntityId = "42",
            OldValues = new { Amount = 10m },
            NewValues = new { Amount = 25m }
        });

        var log = await context.AuditLogs.AsNoTracking().SingleAsync();

        Assert.Equal("CREATE", log.EventType);
        Assert.Equal("Transaction", log.EntityType);
        Assert.Equal("42", log.EntityId);
        Assert.Equal(TestDataSeeder.DefaultUserId, log.UserId);
        Assert.Equal("127.0.0.1", log.IpAddress);
        Assert.Contains("Amount", log.OldValues, StringComparison.Ordinal);
        Assert.Contains("Amount", log.NewValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogAsync_WhenRequestMissingRequiredFields_SkipsPersisting()
    {
        await using var context = TestDbContextFactory.Create();
        var service = new AuditService(
            context,
            new TestUserContextService(TestDataSeeder.DefaultUserId),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<AuditService>.Instance);

        await service.LogAsync(new AuditLogRequest
        {
            EventType = string.Empty,
            EntityType = "Transaction",
            EntityId = "1"
        });

        Assert.Equal(0, await context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task LogAsync_WhenUserContextUnavailable_DoesNotThrow()
    {
        await using var context = TestDbContextFactory.Create();
        var service = new AuditService(
            context,
            new ThrowingUserContextService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<AuditService>.Instance);

        await service.LogAsync(new AuditLogRequest
        {
            EventType = "CREATE",
            EntityType = "Transaction",
            EntityId = "10"
        });

        Assert.Equal(0, await context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task LogAsync_WhenPersistenceFails_DoesNotThrow()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ThrowingSaveChangesDbContext(options);
        var service = new AuditService(
            context,
            new TestUserContextService(TestDataSeeder.DefaultUserId),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<AuditService>.Instance);

        await service.LogAsync(new AuditLogRequest
        {
            EventType = "CREATE",
            EntityType = "Transaction",
            EntityId = "123"
        });

        Assert.DoesNotContain(context.ChangeTracker.Entries(), entry => entry.State != EntityState.Detached);
    }

    private sealed class ThrowingSaveChangesDbContext : ApplicationDbContext
    {
        public ThrowingSaveChangesDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated persistence failure.");
        }
    }
}
