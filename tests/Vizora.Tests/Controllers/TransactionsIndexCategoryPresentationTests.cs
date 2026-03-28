using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Controllers;

public class TransactionsIndexCategoryPresentationTests
{
    [Fact]
    public async Task Index_UsesLinkedCategoryPresentationByCategoryId()
    {
        await using var context = TestDbContextFactory.Create();
        var fitness = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Fitness", TransactionType.Expense);
        fitness.IconKey = "fitness_center";
        fitness.ColorKey = "emerald";

        var transport = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Transport", TransactionType.Expense);
        transport.IconKey = "directions_car";
        transport.ColorKey = "indigo";

        context.Categories.UpdateRange(fitness, transport);
        context.Transactions.AddRange(
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = fitness.Id,
                Type = TransactionType.Expense,
                Amount = 300m,
                Description = "Monthly gym",
                TransactionDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = transport.Id,
                Type = TransactionType.Expense,
                Amount = 80m,
                Description = "Ride fare",
                TransactionDate = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var controller = CreateController(context, TestDataSeeder.DefaultUserId);

        var result = await controller.Index(new TransactionListQuery());

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TransactionsIndexViewModel>(view.Model);
        var fitnessRow = Assert.Single(model.Transactions, t => t.CategoryId == fitness.Id);
        var transportRow = Assert.Single(model.Transactions, t => t.CategoryId == transport.Id);

        Assert.Equal("fitness_center", fitnessRow.CategoryPresentation.IconKey);
        Assert.Equal("emerald", fitnessRow.CategoryPresentation.ColorKey);
        Assert.Equal("Fitness", fitnessRow.CategoryPresentation.Name);

        Assert.Equal("directions_car", transportRow.CategoryPresentation.IconKey);
        Assert.Equal("indigo", transportRow.CategoryPresentation.ColorKey);
        Assert.Equal("Transport", transportRow.CategoryPresentation.Name);
    }

    [Fact]
    public async Task Index_AfterCategoryVisualChange_ReflectsLatestCategoryPresentation()
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
            Amount = 120m,
            Description = "Weekend entertainment",
            TransactionDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, TestDataSeeder.DefaultUserId);
        var firstResult = await controller.Index(new TransactionListQuery());
        var firstView = Assert.IsType<ViewResult>(firstResult);
        var firstModel = Assert.IsType<TransactionsIndexViewModel>(firstView.Model);
        var firstRow = Assert.Single(firstModel.Transactions);
        Assert.Equal("movie", firstRow.CategoryPresentation.IconKey);
        Assert.Equal("purple", firstRow.CategoryPresentation.ColorKey);

        category.IconKey = "pets";
        category.ColorKey = "rose";
        context.Categories.Update(category);
        await context.SaveChangesAsync();

        var secondResult = await controller.Index(new TransactionListQuery());
        var secondView = Assert.IsType<ViewResult>(secondResult);
        var secondModel = Assert.IsType<TransactionsIndexViewModel>(secondView.Model);
        var secondRow = Assert.Single(secondModel.Transactions);
        Assert.Equal("pets", secondRow.CategoryPresentation.IconKey);
        Assert.Equal("rose", secondRow.CategoryPresentation.ColorKey);
    }

    [Fact]
    public async Task Index_WhenCategoryPresentationIsMissing_UsesFallbackOnlyForThatRow()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Utilities", TransactionType.Expense);
        category.IconKey = string.Empty;
        category.ColorKey = string.Empty;
        context.Categories.Update(category);

        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 45m,
            Description = "Utilities payment",
            TransactionDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var persistedTransactions = await context.Transactions.AsNoTracking().ToListAsync();
        Assert.Single(persistedTransactions);

        var controller = CreateController(context, TestDataSeeder.DefaultUserId);

        var result = await controller.Index(new TransactionListQuery());

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TransactionsIndexViewModel>(view.Model);
        var row = Assert.Single(model.Transactions);
        Assert.Equal("Utilities", row.CategoryPresentation.Name);
        Assert.Equal(CategoryVisualCatalog.DefaultIconKey, row.CategoryPresentation.IconKey);
        Assert.Equal(CategoryVisualCatalog.DefaultColorKey, row.CategoryPresentation.ColorKey);
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

        var userContext = new HttpContextUserContextService(accessor);
        var categoryService = new CategoryService(
            context,
            userContext,
            new NoOpAuditService(),
            NullLogger<CategoryService>.Instance);
        var transactionService = new TransactionService(
            context,
            userContext,
            new NoOpAuditService(),
            NullLogger<TransactionService>.Instance);

        return new TransactionsController(
            transactionService,
            categoryService,
            new StubTransactionImportService(),
            NullLogger<TransactionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class StubTransactionImportService : ITransactionImportService
    {
        public Task<TransactionImportResultDto> ImportCsvAsync(IFormFile file)
        {
            throw new NotSupportedException();
        }
    }
}
