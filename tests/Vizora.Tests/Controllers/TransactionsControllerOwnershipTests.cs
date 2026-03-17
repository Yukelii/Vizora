using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Controllers;

public class TransactionsControllerOwnershipTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task Details_WhenTransactionBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.Create();
        var otherCategory = TestDataSeeder.EnsureCategory(context, OtherUserId, "Other", TransactionType.Expense);
        var transaction = new Transaction
        {
            UserId = OtherUserId,
            CategoryId = otherCategory.Id,
            Type = TransactionType.Expense,
            Amount = 45m,
            Description = "Other tenant transaction",
            TransactionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var controller = CreateController(context, TestDataSeeder.DefaultUserId);
        var result = await controller.Details(transaction.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_WhenTransactionBelongsToCurrentUser_ReturnsView()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var transaction = new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 25m,
            Description = "Lunch",
            TransactionDate = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var controller = CreateController(context, TestDataSeeder.DefaultUserId);
        var result = await controller.Details(transaction.Id);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<Transaction>(viewResult.Model);
        Assert.Equal(transaction.Id, model.Id);
        Assert.Equal(TestDataSeeder.DefaultUserId, model.UserId);
    }

    private static TransactionsController CreateController(ApplicationDbContext context, string userId)
    {
        var userClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(userClaims, authenticationType: "TestAuth"))
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var transactionService = new TransactionService(
            context,
            new HttpContextUserContextService(accessor),
            new NoOpAuditService(),
            NullLogger<TransactionService>.Instance);

        var controller = new TransactionsController(
            transactionService,
            new StubCategoryService(),
            new StubTransactionImportService(),
            NullLogger<TransactionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return controller;
    }

    private sealed class StubCategoryService : ICategoryService
    {
        public Task<IReadOnlyList<Category>> GetAllAsync()
        {
            return Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>());
        }

        public Task<Category?> GetByIdAsync(int id)
        {
            return Task.FromResult<Category?>(null);
        }

        public Task CreateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(int id)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubTransactionImportService : ITransactionImportService
    {
        public Task<TransactionImportResultDto> ImportCsvAsync(IFormFile file)
        {
            throw new NotSupportedException();
        }
    }
}
