using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class TransactionReportServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task ExportTransactionsCsvAsync_ReturnsOnlyCurrentUsersRows()
    {
        await using var context = TestDbContextFactory.Create();
        var userCategory = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);

        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = userCategory.Id,
                Type = TransactionType.Expense,
                Amount = 12.50m,
                Description = "User lunch",
                TransactionDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = OtherUserId,
                CategoryId = otherCategory.Id,
                Type = TransactionType.Expense,
                Amount = 99m,
                Description = "Other user row",
                TransactionDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var csv = Encoding.UTF8.GetString(await service.ExportTransactionsCsvAsync());
        var lines = ToLines(csv);

        Assert.Equal("TransactionId,TransactionDate,Type,Category,Amount,Description", lines[0]);
        Assert.Equal(2, lines.Count);
        Assert.Contains("User lunch", csv);
        Assert.DoesNotContain("Other user row", csv);
    }

    [Fact]
    public async Task ExportTransactionsCsvAsync_SanitizesFormulaInjectionPayloads()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "=Danger", TransactionType.Expense);
        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 5m,
            Description = "=SUM(A1:A2)",
            TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var csv = Encoding.UTF8.GetString(await service.ExportTransactionsCsvAsync());

        Assert.Contains("\"'=Danger\"", csv);
        Assert.Contains("\"'=SUM(A1:A2)\"", csv);
    }

    [Fact]
    public async Task ExportTransactionsCsvAsync_WhenNoTransactions_ReturnsHeaderOnly()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);

        var csv = Encoding.UTF8.GetString(await service.ExportTransactionsCsvAsync());
        var lines = ToLines(csv);

        Assert.Single(lines);
        Assert.Equal("TransactionId,TransactionDate,Type,Category,Amount,Description", lines[0]);
    }

    [Fact]
    public async Task ExportTransactionsCsvAsync_SortsByTransactionDateDescendingThenCreatedAt()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 9m,
                Description = "Older",
                TransactionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 11m,
                Description = "Newer",
                TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc),
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var csv = Encoding.UTF8.GetString(await service.ExportTransactionsCsvAsync());
        var lines = ToLines(csv);

        Assert.Equal(3, lines.Count);
        Assert.Contains("Newer", lines[1]);
        Assert.Contains("Older", lines[2]);
    }

    private static TransactionReportService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new TransactionReportService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<TransactionReportService>.Instance);
    }

    private static List<string> ToLines(string csv)
    {
        return csv
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToList();
    }
}
