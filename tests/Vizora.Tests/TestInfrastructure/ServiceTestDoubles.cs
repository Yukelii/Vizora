using Vizora.Services;

namespace Vizora.Tests.TestInfrastructure;

public sealed class TestUserContextService : IUserContextService
{
    private readonly string _userId;

    public TestUserContextService(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        _userId = userId;
    }

    public string GetRequiredUserId()
    {
        return _userId;
    }
}

public sealed class NoOpAuditService : IAuditService
{
    public Task LogAsync(AuditLogRequest request)
    {
        return Task.CompletedTask;
    }
}

public sealed class ThrowingAuditService : IAuditService
{
    public Task LogAsync(AuditLogRequest request)
    {
        throw new InvalidOperationException("Simulated audit failure.");
    }
}

public sealed class ThrowingUserContextService : IUserContextService
{
    public string GetRequiredUserId()
    {
        throw new UnauthorizedAccessException("Simulated missing user context.");
    }
}
