using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Controllers;

public class BudgetsControllerModalTests
{
    [Fact]
    public async Task Edit_WhenRowVersionIsMissingForModal_ReturnsRecoveryValidationAndKeepsModalEditable()
    {
        var latestRowVersion = Guid.NewGuid().ToByteArray();
        var budgetService = new StubBudgetService
        {
            Budget = new Budget
            {
                Id = 13,
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = 7,
                PlannedAmount = 1000m,
                BudgetPeriodId = 21,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = latestRowVersion,
                BudgetPeriod = new BudgetPeriod
                {
                    Id = 21,
                    UserId = TestDataSeeder.DefaultUserId,
                    Type = BudgetPeriodType.Monthly,
                    StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                    CreatedAt = DateTime.UtcNow
                }
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

        var controller = CreateController(budgetService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new BudgetUpsertViewModel
        {
            Id = 13,
            RowVersion = null,
            CategoryId = 7,
            PlannedAmount = 1000m,
            PeriodType = BudgetPeriodType.Monthly,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31)
        };

        var result = await controller.Edit(13, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.False(budgetService.UpdateCalled);
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
        var budgetService = new StubBudgetService
        {
            Budget = new Budget
            {
                Id = 13,
                UserId = TestDataSeeder.DefaultUserId,
                CategoryId = 7,
                PlannedAmount = 1000m,
                BudgetPeriodId = 21,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = latestRowVersion,
                BudgetPeriod = new BudgetPeriod
                {
                    Id = 21,
                    UserId = TestDataSeeder.DefaultUserId,
                    Type = BudgetPeriodType.Monthly,
                    StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                    CreatedAt = DateTime.UtcNow
                }
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

        var controller = CreateController(budgetService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new BudgetUpsertViewModel
        {
            Id = 13,
            RowVersion = "not-hex",
            CategoryId = 7,
            PlannedAmount = 1000m,
            PeriodType = BudgetPeriodType.Monthly,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31)
        };

        var result = await controller.Edit(13, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.False(budgetService.UpdateCalled);
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
        var budgetService = new StubBudgetService();
        var categoryService = new StubCategoryService();
        var controller = CreateController(budgetService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new BudgetUpsertViewModel
        {
            Id = 13,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CategoryId = 7,
            PlannedAmount = 1000m,
            PeriodType = BudgetPeriodType.Monthly,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31)
        };

        var result = await controller.Edit(13, model);

        Assert.IsType<JsonResult>(result);
        Assert.True(budgetService.UpdateCalled);
        Assert.Equal(ModalUiState.Success, controller.ViewData["ModalState"]);
    }

    [Fact]
    public async Task Edit_WhenServiceReturnsConflict_ShowsConflictBannerWithoutDuplicateSummaryError()
    {
        var databaseRowVersionHex = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var budgetService = new StubBudgetService
        {
            UpdateResult = UpdateOperationResult<BudgetConflictSnapshot>.ConflictDetected(
                new ConcurrencyConflictResult<BudgetConflictSnapshot>
                {
                    CurrentValues = new BudgetConflictSnapshot
                    {
                        RowVersionHex = "01",
                        CategoryId = 7,
                        PlannedAmount = 1000m,
                        PeriodType = BudgetPeriodType.Monthly,
                        StartDate = new DateTime(2026, 3, 1),
                        EndDate = new DateTime(2026, 3, 31)
                    },
                    DatabaseValues = new BudgetConflictSnapshot
                    {
                        RowVersionHex = databaseRowVersionHex,
                        CategoryId = 7,
                        PlannedAmount = 1200m,
                        PeriodType = BudgetPeriodType.Monthly,
                        StartDate = new DateTime(2026, 3, 1),
                        EndDate = new DateTime(2026, 3, 31)
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

        var controller = CreateController(budgetService, categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new BudgetUpsertViewModel
        {
            Id = 13,
            RowVersion = "AA",
            CategoryId = 7,
            PlannedAmount = 1000m,
            PeriodType = BudgetPeriodType.Monthly,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 31)
        };

        var result = await controller.Edit(13, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(budgetService.UpdateCalled);
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

    private static BudgetsController CreateController(
        IBudgetService budgetService,
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

        var controller = new BudgetsController(
            budgetService,
            categoryService,
            NullLogger<BudgetsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new StubUrlHelper(httpContext)
        };

        return controller;
    }

    private sealed class StubBudgetService : IBudgetService
    {
        public bool UpdateCalled { get; private set; }

        public Budget? Budget { get; set; }

        public UpdateOperationResult<BudgetConflictSnapshot> UpdateResult { get; set; } =
            UpdateOperationResult<BudgetConflictSnapshot>.Success();

        public Task<IReadOnlyList<BudgetPerformanceViewModel>> GetAllWithPerformanceAsync(DateTime? filterStartDate = null, DateTime? filterEndDate = null)
        {
            return Task.FromResult<IReadOnlyList<BudgetPerformanceViewModel>>(Array.Empty<BudgetPerformanceViewModel>());
        }

        public Task<BudgetPerformanceViewModel?> GetPerformanceByIdAsync(int id)
        {
            return Task.FromResult<BudgetPerformanceViewModel?>(null);
        }

        public Task<Budget?> GetByIdAsync(int id)
        {
            return Task.FromResult(Budget);
        }

        public Task CreateAsync(BudgetUpsertRequest request)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateOperationResult<BudgetConflictSnapshot>> UpdateAsync(int id, BudgetUpsertRequest request, bool forceOverwrite = false)
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
            var controller = actionContext.Controller ?? "Budgets";
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
