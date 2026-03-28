using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Controllers;

public class TransactionsControllerModalTests
{
    [Fact]
    public async Task Edit_WhenModelStateInvalidForModal_ReturnsValidationStateAndRepopulatesCategoryOptions()
    {
        var categoryService = new StubCategoryService
        {
            Categories =
            {
                new Category
                {
                    Id = 5,
                    UserId = TestDataSeeder.DefaultUserId,
                    Name = "Food",
                    Type = TransactionType.Expense,
                    IconKey = CategoryVisualCatalog.DefaultIconKey,
                    ColorKey = CategoryVisualCatalog.DefaultColorKey,
                    CreatedAt = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                }
            }
        };
        var transactionService = new StubTransactionService();
        var controller = CreateController(transactionService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 12,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CategoryId = 5,
            Amount = 0,
            Description = "Lunch",
            TransactionDate = new DateTime(2026, 3, 1)
        };
        controller.ModelState.AddModelError(nameof(TransactionUpsertViewModel.Amount), "Amount must be greater than zero.");

        var result = await controller.Edit(12, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.False(transactionService.UpdateCalled);
        Assert.IsType<SelectList>(controller.ViewData["CategoryId"]);
    }

    [Fact]
    public async Task Edit_WhenRouteAndModelIdsMismatchForModal_ReturnsValidationModalState()
    {
        var controller = CreateController(
            new StubTransactionService(),
            new StubCategoryService(),
            TestDataSeeder.DefaultUserId,
            isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 22,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CategoryId = 1,
            Amount = 50m,
            Description = "Mismatch",
            TransactionDate = new DateTime(2026, 3, 2)
        };

        var result = await controller.Edit(23, model);

        Assert.IsType<PartialViewResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsMissingForModal_ReturnsRecoveryValidationAndKeepsModalEditable()
    {
        var latestRowVersion = Guid.NewGuid().ToByteArray();
        var transactionService = new StubTransactionService
        {
            Transaction = new Transaction
            {
                Id = 31,
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = 7,
                Type = TransactionType.Expense,
                Amount = 120m,
                Description = "Groceries",
                TransactionDate = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = latestRowVersion
            }
        };
        var categoryService = new StubCategoryService
        {
            Categories =
            {
                new Category
                {
                    Id = 7,
                    UserId = TestDataSeeder.DefaultUserId,
                    Name = "Food",
                    Type = TransactionType.Expense,
                    IconKey = CategoryVisualCatalog.DefaultIconKey,
                    ColorKey = CategoryVisualCatalog.DefaultColorKey,
                    CreatedAt = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                }
            }
        };
        var controller = CreateController(transactionService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 31,
            RowVersion = null,
            CategoryId = 7,
            Amount = 120m,
            Description = "Groceries",
            TransactionDate = new DateTime(2026, 3, 4)
        };

        var result = await controller.Edit(31, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.False(transactionService.UpdateCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.Equal(Convert.ToHexString(latestRowVersion), model.RowVersion);
        Assert.IsType<SelectList>(controller.ViewData["CategoryId"]);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            controller.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("invalid or expired", StringComparison.OrdinalIgnoreCase));
        Assert.False(controller.ViewData.ContainsKey("ModalConflictBanner"));
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsInvalidForModal_ReturnsRecoveryValidationAndKeepsModalEditable()
    {
        var latestRowVersion = Guid.NewGuid().ToByteArray();
        var transactionService = new StubTransactionService
        {
            Transaction = new Transaction
            {
                Id = 31,
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = 7,
                Type = TransactionType.Expense,
                Amount = 120m,
                Description = "Groceries",
                TransactionDate = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = latestRowVersion
            }
        };
        var categoryService = new StubCategoryService
        {
            Categories =
            {
                new Category
                {
                    Id = 7,
                    UserId = TestDataSeeder.DefaultUserId,
                    Name = "Food",
                    Type = TransactionType.Expense,
                    IconKey = CategoryVisualCatalog.DefaultIconKey,
                    ColorKey = CategoryVisualCatalog.DefaultColorKey,
                    CreatedAt = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                }
            }
        };
        var controller = CreateController(transactionService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 31,
            RowVersion = "not-hex",
            CategoryId = 7,
            Amount = 120m,
            Description = "Groceries",
            TransactionDate = new DateTime(2026, 3, 4)
        };

        var result = await controller.Edit(31, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.False(transactionService.UpdateCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.Equal(Convert.ToHexString(latestRowVersion), model.RowVersion);
        Assert.IsType<SelectList>(controller.ViewData["CategoryId"]);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            controller.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("invalid or expired", StringComparison.OrdinalIgnoreCase));
        Assert.False(controller.ViewData.ContainsKey("ModalConflictBanner"));
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsValidAndServiceSucceeds_ReturnsModalSuccess()
    {
        var transactionService = new StubTransactionService();
        var categoryService = new StubCategoryService();
        var controller = CreateController(transactionService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 31,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CategoryId = 7,
            Amount = 120m,
            Description = "Groceries",
            TransactionDate = new DateTime(2026, 3, 4)
        };

        var result = await controller.Edit(31, model);

        Assert.IsType<JsonResult>(result);
        Assert.True(transactionService.UpdateCalled);
        Assert.Equal(ModalUiState.Success, controller.ViewData["ModalState"]);
    }

    [Fact]
    public async Task Edit_WhenServiceReturnsConflict_ShowsConflictBannerWithoutDuplicateSummaryError()
    {
        var databaseRowVersionHex = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var transactionService = new StubTransactionService
        {
            UpdateResult = UpdateOperationResult<TransactionConflictSnapshot>.ConflictDetected(
                new ConcurrencyConflictResult<TransactionConflictSnapshot>
                {
                    CurrentValues = new TransactionConflictSnapshot
                    {
                        RowVersionHex = "01",
                        CategoryId = 7,
                        Amount = 120m,
                        Description = "Groceries",
                        TransactionDate = new DateTime(2026, 3, 4)
                    },
                    DatabaseValues = new TransactionConflictSnapshot
                    {
                        RowVersionHex = databaseRowVersionHex,
                        CategoryId = 7,
                        Amount = 150m,
                        Description = "Groceries and snacks",
                        TransactionDate = new DateTime(2026, 3, 5)
                    }
                },
                "This record is out of sync. Reload the latest values and try again.")
        };
        var categoryService = new StubCategoryService
        {
            Categories =
            {
                new Category
                {
                    Id = 7,
                    UserId = TestDataSeeder.DefaultUserId,
                    Name = "Food",
                    Type = TransactionType.Expense,
                    IconKey = CategoryVisualCatalog.DefaultIconKey,
                    ColorKey = CategoryVisualCatalog.DefaultColorKey,
                    CreatedAt = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                }
            }
        };
        var controller = CreateController(transactionService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new TransactionUpsertViewModel
        {
            Id = 31,
            RowVersion = "AA",
            CategoryId = 7,
            Amount = 120m,
            Description = "Groceries",
            TransactionDate = new DateTime(2026, 3, 4)
        };

        var result = await controller.Edit(31, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(transactionService.UpdateCalled);
        Assert.Equal(StatusCodes.Status409Conflict, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.Conflict, controller.ViewData["ModalState"]);
        Assert.Equal(databaseRowVersionHex, model.RowVersion);
        Assert.False(
            controller.ModelState.TryGetValue(string.Empty, out var summaryEntry) &&
            summaryEntry.Errors.Count > 0);
        var banner = Assert.IsType<ModalConflictBannerViewModel>(controller.ViewData["ModalConflictBanner"]);
        Assert.Contains("out of sync", banner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(banner.AllowOverwrite);
    }

    private static TransactionsController CreateController(
        ITransactionService transactionService,
        ICategoryService categoryService,
        string userId,
        bool isModalRequest)
    {
        var userClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(userClaims, authenticationType: "TestAuth"))
        };

        if (isModalRequest)
        {
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        }

        var controller = new TransactionsController(
            transactionService,
            categoryService,
            new StubTransactionImportService(),
            NullLogger<TransactionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new StubUrlHelper(httpContext)
        };

        return controller;
    }

    private sealed class StubTransactionService : ITransactionService
    {
        public bool UpdateCalled { get; private set; }

        public Transaction? Transaction { get; set; }

        public UpdateOperationResult<TransactionConflictSnapshot> UpdateResult { get; set; } =
            UpdateOperationResult<TransactionConflictSnapshot>.Success();

        public Task<IReadOnlyList<Transaction>> GetAllAsync()
        {
            return Task.FromResult<IReadOnlyList<Transaction>>(Array.Empty<Transaction>());
        }

        public Task<PagedResult<Transaction>> GetPagedAsync(TransactionListQuery query)
        {
            return Task.FromResult(new PagedResult<Transaction>());
        }

        public Task<Transaction?> GetByIdAsync(int id)
        {
            return Task.FromResult(Transaction);
        }

        public Task CreateAsync(Transaction transaction)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateOperationResult<TransactionConflictSnapshot>> UpdateAsync(Transaction transaction, bool forceOverwrite = false)
        {
            UpdateCalled = true;
            return Task.FromResult(UpdateResult);
        }

        public Task<bool> DeleteAsync(int id)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubCategoryService : ICategoryService
    {
        public List<Category> Categories { get; } = new();

        public Task<IReadOnlyList<Category>> GetAllAsync(CategoryListFilter filter = CategoryListFilter.All)
        {
            IReadOnlyList<Category> source = Categories;
            if (filter == CategoryListFilter.Expense)
            {
                source = Categories.Where(c => c.Type == TransactionType.Expense).ToList();
            }
            else if (filter == CategoryListFilter.Income)
            {
                source = Categories.Where(c => c.Type == TransactionType.Income).ToList();
            }

            return Task.FromResult(source);
        }

        public Task<Category?> GetByIdAsync(int id)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id));
        }

        public Task CreateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateOperationResult<CategoryConflictSnapshot>> UpdateAsync(Category category, bool forceOverwrite = false)
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

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(HttpContext httpContext)
        {
            ActionContext = new ActionContext
            {
                HttpContext = httpContext
            };
        }

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext)
        {
            var controller = actionContext.Controller ?? "Transactions";
            var action = actionContext.Action ?? "Index";
            return $"/{controller}/{action}";
        }

        public string? Content(string? contentPath)
        {
            return contentPath;
        }

        public bool IsLocalUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   (url.StartsWith("/", StringComparison.Ordinal) || url.StartsWith("~/", StringComparison.Ordinal));
        }

        public string? Link(string? routeName, object? values)
        {
            return Action(new UrlActionContext { Action = routeName, Values = values });
        }

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            return "/route";
        }
    }
}
