using Vizora.Enums;
using Vizora.Models;
using Vizora.Data;

namespace Vizora.Tests.TestInfrastructure;

public static class TestDataSeeder
{
    public const string DefaultUserId = "test-user-1";

    public static ApplicationUser EnsureUser(ApplicationDbContext context, string userId = DefaultUserId)
    {
        var existing = context.Users.FirstOrDefault(u => u.Id == userId);
        if (existing != null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"{userId}@vizora.test",
            NormalizedUserName = $"{userId}@vizora.test".ToUpperInvariant(),
            Email = $"{userId}@vizora.test",
            NormalizedEmail = $"{userId}@vizora.test".ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

        context.Users.Add(user);
        context.SaveChanges();

        return user;
    }

    public static Category EnsureCategory(
        ApplicationDbContext context,
        string userId = DefaultUserId,
        string? categoryName = null,
        TransactionType type = TransactionType.Expense)
    {
        EnsureUser(context, userId);
        var name = categoryName ?? "Groceries";

        var existing = context.Categories.FirstOrDefault(c =>
            c.UserId == userId &&
            c.Name == name &&
            c.Type == type);

        if (existing != null)
        {
            return existing;
        }

        var category = new Category
        {
            UserId = userId,
            Name = name,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };

        context.Categories.Add(category);
        context.SaveChanges();

        return category;
    }

    public static IReadOnlyList<Transaction> SeedTransactions(
        ApplicationDbContext context,
        string userId = DefaultUserId)
    {
        var category = EnsureCategory(context, userId);
        var baseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var transactions = new List<Transaction>
        {
            new()
            {
                UserId = userId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 100.00m,
                Description = "Test transaction 1",
                TransactionDate = baseDate,
                CreatedAt = baseDate,
                UpdatedAt = baseDate
            },
            new()
            {
                UserId = userId,
                CategoryId = category.Id,
                Type = TransactionType.Expense,
                Amount = 42.50m,
                Description = "Test transaction 2",
                TransactionDate = baseDate.AddDays(1),
                CreatedAt = baseDate.AddDays(1),
                UpdatedAt = baseDate.AddDays(1)
            }
        };

        context.Transactions.AddRange(transactions);
        context.SaveChanges();

        return transactions;
    }
}
