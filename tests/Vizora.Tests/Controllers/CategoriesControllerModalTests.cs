using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Controllers;

public class CategoriesControllerModalTests
{
    [Fact]
    public async Task Edit_WhenModelStateInvalidForModal_ReturnsValidationStateAndPreservesSubmittedVisualSelections()
    {
        var categoryService = new StubCategoryService();
        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 42,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = "invalid_icon",
            ColorKey = "invalid_color"
        };
        controller.ModelState.AddModelError(nameof(CategoryUpsertViewModel.IconKey), "Selected icon is not supported.");
        controller.ModelState.AddModelError(nameof(CategoryUpsertViewModel.ColorKey), "Selected color is not supported.");

        var result = await controller.Edit(42, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.False(categoryService.UpdateCalled);
        Assert.Equal("invalid_icon", model.IconKey);
        Assert.Equal("invalid_color", model.ColorKey);
        Assert.True(controller.ModelState.ContainsKey(nameof(CategoryUpsertViewModel.IconKey)));
        Assert.True(controller.ModelState.ContainsKey(nameof(CategoryUpsertViewModel.ColorKey)));
    }

    [Fact]
    public async Task Edit_WhenRouteAndModelIdsMismatchForModal_ReturnsValidationModalState()
    {
        var categoryService = new StubCategoryService();
        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 10,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey
        };

        var result = await controller.Edit(11, model);

        Assert.IsType<PartialViewResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsMissingForModal_ReturnsRecoveryValidationWithoutCallingService()
    {
        var categoryService = new StubCategoryService();
        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 42,
            RowVersion = null,
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey
        };
        categoryService.Category = new Category
        {
            Id = 42,
            UserId = TestDataSeeder.DefaultUserId,
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey,
            CreatedAt = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var result = await controller.Edit(42, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.False(categoryService.UpdateCalled);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            controller.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("invalid or expired", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Convert.ToHexString(categoryService.Category.RowVersion), model.RowVersion);
        Assert.False(controller.ViewData.ContainsKey("ModalConflictBanner"));
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsInvalidForModal_ReturnsRecoveryValidationWithoutCallingService()
    {
        var categoryService = new StubCategoryService();
        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 42,
            RowVersion = "not-hex",
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey
        };
        categoryService.Category = new Category
        {
            Id = 42,
            UserId = TestDataSeeder.DefaultUserId,
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey,
            CreatedAt = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var result = await controller.Edit(42, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.ValidationError, controller.ViewData["ModalState"]);
        Assert.False(categoryService.UpdateCalled);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            controller.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("invalid or expired", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Convert.ToHexString(categoryService.Category.RowVersion), model.RowVersion);
        Assert.False(controller.ViewData.ContainsKey("ModalConflictBanner"));
    }

    [Fact]
    public async Task Edit_WhenServiceReturnsConflict_UsesDatabaseRowVersionAndConflictState()
    {
        var databaseRowVersionHex = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var categoryService = new StubCategoryService
        {
            UpdateResult = UpdateOperationResult<CategoryConflictSnapshot>.ConflictDetected(
                new ConcurrencyConflictResult<CategoryConflictSnapshot>
                {
                    CurrentValues = new CategoryConflictSnapshot
                    {
                        RowVersionHex = "01",
                        Name = "Food",
                        Type = TransactionType.Expense,
                        IconKey = CategoryVisualCatalog.DefaultIconKey,
                        ColorKey = CategoryVisualCatalog.DefaultColorKey
                    },
                    DatabaseValues = new CategoryConflictSnapshot
                    {
                        RowVersionHex = databaseRowVersionHex,
                        Name = "Food",
                        Type = TransactionType.Expense,
                        IconKey = CategoryVisualCatalog.DefaultIconKey,
                        ColorKey = CategoryVisualCatalog.DefaultColorKey
                    }
                },
                "This record is out of sync. Reload the latest values and try again.")
        };

        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 7,
            RowVersion = "AA",
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey
        };

        var result = await controller.Edit(7, model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(categoryService.UpdateCalled);
        Assert.Equal(StatusCodes.Status409Conflict, controller.Response.StatusCode);
        Assert.Equal(ModalUiState.Conflict, controller.ViewData["ModalState"]);
        Assert.Equal(databaseRowVersionHex, model.RowVersion);
        Assert.False(model.ForceOverwrite);
        Assert.False(
            controller.ModelState.TryGetValue(string.Empty, out var summaryEntry) &&
            summaryEntry.Errors.Count > 0);

        var banner = Assert.IsType<ModalConflictBannerViewModel>(controller.ViewData["ModalConflictBanner"]);
        Assert.True(banner.AllowOverwrite);
        Assert.NotEmpty(banner.FieldComparisons);
    }

    [Fact]
    public async Task Edit_WhenRowVersionIsValidAndServiceSucceeds_ReturnsModalSuccess()
    {
        var categoryService = new StubCategoryService();
        var controller = CreateController(categoryService, TestDataSeeder.DefaultUserId, isModalRequest: true);
        var model = new CategoryUpsertViewModel
        {
            Id = 42,
            RowVersion = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Name = "Food",
            Type = TransactionType.Expense,
            IconKey = CategoryVisualCatalog.DefaultIconKey,
            ColorKey = CategoryVisualCatalog.DefaultColorKey
        };

        var result = await controller.Edit(42, model);

        Assert.IsType<JsonResult>(result);
        Assert.True(categoryService.UpdateCalled);
        Assert.Equal(ModalUiState.Success, controller.ViewData["ModalState"]);
    }

    private static CategoriesController CreateController(
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

        var controller = new CategoriesController(categoryService, NullLogger<CategoriesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new StubUrlHelper(httpContext)
        };

        return controller;
    }

    private sealed class StubCategoryService : ICategoryService
    {
        public bool UpdateCalled { get; private set; }

        public Category? Category { get; set; }

        public UpdateOperationResult<CategoryConflictSnapshot> UpdateResult { get; set; } =
            UpdateOperationResult<CategoryConflictSnapshot>.Success();

        public Task<IReadOnlyList<Category>> GetAllAsync(CategoryListFilter filter = CategoryListFilter.All)
        {
            return Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>());
        }

        public Task<Category?> GetByIdAsync(int id)
        {
            return Task.FromResult(Category);
        }

        public Task CreateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateOperationResult<CategoryConflictSnapshot>> UpdateAsync(Category category, bool forceOverwrite = false)
        {
            UpdateCalled = true;
            return Task.FromResult(UpdateResult);
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
            var controller = actionContext.Controller ?? "Categories";
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
