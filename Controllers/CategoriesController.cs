using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class CategoriesController : Controller
    {
        private readonly ICategoryService _categoryService;
        private readonly ILogger<CategoriesController> _logger;

        public CategoriesController(
            ICategoryService categoryService,
            ILogger<CategoriesController> logger)
        {
            _categoryService = categoryService;
            _logger = logger;
        }

        public async Task<IActionResult> Index([FromQuery] CategoryListQuery query)
        {
            if (!Enum.IsDefined(typeof(CategoryListFilter), query.Filter))
            {
                query.Filter = CategoryListFilter.All;
            }

            var allCategories = await _categoryService.GetAllAsync();
            var categories = query.Filter == CategoryListFilter.All
                ? allCategories
                : await _categoryService.GetAllAsync(query.Filter);

            var model = new CategoriesIndexViewModel
            {
                Categories = categories,
                Filter = query.Filter,
                TotalCategories = allCategories.Count,
                ExpenseCategories = allCategories.Count(c => c.Type == TransactionType.Expense),
                IncomeCategories = allCategories.Count(c => c.Type == TransactionType.Income)
            };

            return View(model);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var category = await _categoryService.GetByIdAsync(id.Value);
            if (category == null)
            {
                return NotFound();
            }

            return View(category);
        }

        public IActionResult Create()
        {
            return View(new CategoryUpsertViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryUpsertViewModel model)
        {
            // Keep UI validation and business-rule validation separate.
            if (!ModelState.IsValid)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", model.Id, ModalFailureType.Validation);
                return View(model);
            }

            var category = new Category
            {
                Name = model.Name,
                Type = model.Type,
                IconKey = model.IconKey,
                ColorKey = model.ColorKey
            };

            try
            {
                await _categoryService.CreateAsync(category);
                return this.IsModalRequest()
                    ? this.ModalSuccess()
                    : RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Surface domain-rule violations (for example duplicate category name/type).
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", model.Id, ModalFailureType.Validation);
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "category", model.Id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to create category due to an unexpected error. Please try again.");
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var category = await _categoryService.GetByIdAsync(id.Value);
            if (category == null)
            {
                return NotFound();
            }

            var model = new CategoryUpsertViewModel
            {
                Id = category.Id,
                RowVersion = Convert.ToHexString(category.RowVersion),
                Name = category.Name,
                Type = category.Type,
                IconKey = CategoryVisualCatalog.ResolveIconKeyOrDefault(category.IconKey),
                ColorKey = CategoryVisualCatalog.ResolveColorKeyOrDefault(category.ColorKey)
            };

            this.LogModalLifecycle(_logger, "category", id, "edit_form_loaded");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CategoryUpsertViewModel model)
        {
            this.LogModalLifecycle(_logger, "category", id, "edit_submit_attempted");

            if (id != model.Id)
            {
                if (this.IsModalRequest())
                {
                    this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, renderedInModalResponse: true);
                    return this.ModalError(
                        "Unable to update category because the request did not match the selected record.",
                        statusCode: StatusCodes.Status400BadRequest,
                        outcome: ModalUiState.ValidationError,
                        state: ModalUiState.ValidationError);
                }

                return NotFound();
            }

            // Missing/invalid tokens indicate an invalid edit session, not a true concurrency conflict.
            ModelState.Remove(nameof(model.RowVersion));
            if (!TryDecodeRowVersion(model.RowVersion, out var rowVersion))
            {
                const string invalidEditStateMessage = "This edit state is invalid or expired. Reload the latest values and try again.";
                var latestCategory = await _categoryService.GetByIdAsync(id);
                if (latestCategory == null)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "category", id, ModalFailureType.Error, renderedInModalResponse: true);
                        return this.ModalError(
                            "This category no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, renderedInModalResponse: true);
                ModelState.AddModelError(
                    string.Empty,
                    invalidEditStateMessage);
                model.RowVersion = Convert.ToHexString(latestCategory.RowVersion);
                model.ForceOverwrite = false;
                ModelState.Remove(nameof(model.RowVersion));
                ModelState.Remove(nameof(model.ForceOverwrite));
                this.ClearModalConflictBanner();
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, renderedInModalResponse: true);
                return View(model);
            }

            var category = new Category
            {
                Id = id,
                RowVersion = rowVersion,
                Name = model.Name,
                Type = model.Type,
                IconKey = model.IconKey,
                ColorKey = model.ColorKey
            };

            try
            {
                var updateResult = await _categoryService.UpdateAsync(category, model.ForceOverwrite);
                if (updateResult.Status == UpdateOperationStatus.NotFound)
                {
                    // Not found is possible when record belongs to another user or was deleted.
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "category", id, ModalFailureType.Error, renderedInModalResponse: true);
                        return this.ModalError(
                            "This category no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                    this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, renderedInModalResponse: true);
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update category.");
                    return View(model);
                }

                if (updateResult.Status == UpdateOperationStatus.Conflict && updateResult.Conflict != null)
                {
                    this.SetModalStateAndStatus(ModalUiState.Conflict, StatusCodes.Status409Conflict);
                    this.LogModalFailure(_logger, "category", id, ModalFailureType.Concurrency, renderedInModalResponse: true);
                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    this.SetModalConflictBanner(
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.",
                        Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                        "Edit Category",
                        BuildCategoryConflictComparisons(
                            updateResult.Conflict.CurrentValues,
                            updateResult.Conflict.DatabaseValues),
                        allowOverwrite: true);
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                    this.LogModalFailure(_logger, "category", id, ModalFailureType.Error, renderedInModalResponse: true);
                    ModelState.AddModelError(string.Empty, "Unable to update category.");
                    return View(model);
                }

                if (this.IsModalRequest())
                {
                    this.LogModalLifecycle(_logger, "category", id, "edit_submit_succeeded");
                    return this.ModalSuccess("Category updated successfully.");
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, renderedInModalResponse: true);
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Error, ex, renderedInModalResponse: true);
                ModelState.AddModelError(string.Empty, "Unable to update category due to an unexpected error. Please try again.");
                return View(model);
            }
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var category = await _categoryService.GetByIdAsync(id.Value);
            if (category == null)
            {
                return NotFound();
            }

            return View(category);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var deleted = await _categoryService.DeleteAsync(id);
                if (!deleted)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "category", id, ModalFailureType.Error);
                        return this.ModalError("This category no longer exists or you no longer have access to it.");
                    }

                    return NotFound();
                }

                return this.IsModalRequest()
                    ? this.ModalSuccess()
                    : RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Preserve user feedback when deletion is blocked by dependency checks.
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation);
                ModelState.AddModelError(string.Empty, ex.Message);
                var category = await _categoryService.GetByIdAsync(id);
                if (category == null)
                {
                    return this.IsModalRequest()
                        ? this.ModalError("Unable to load this category after the failed delete request.")
                        : NotFound();
                }

                return View("Delete", category);
            }
            catch (DbUpdateException ex)
            {
                // Final safety net for FK references missed by pre-delete validation.
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Validation, ex);
                ModelState.AddModelError(
                    string.Empty,
                    "Category cannot be deleted because it is referenced by other records.");

                var category = await _categoryService.GetByIdAsync(id);
                if (category == null)
                {
                    return this.IsModalRequest()
                        ? this.ModalError("Unable to load this category after the failed delete request.")
                        : NotFound();
                }

                return View("Delete", category);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "category", id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to delete category due to an unexpected error. Please try again.");

                var category = await _categoryService.GetByIdAsync(id);
                if (category == null)
                {
                    return this.IsModalRequest()
                        ? this.ModalError("Unable to load this category after the failed delete request.")
                        : NotFound();
                }

                return View("Delete", category);
            }
        }

        private static IList<ConcurrencyFieldComparisonViewModel> BuildCategoryConflictComparisons(
            CategoryConflictSnapshot currentValues,
            CategoryConflictSnapshot latestValues)
        {
            return new List<ConcurrencyFieldComparisonViewModel>
            {
                new()
                {
                    FieldLabel = "Name",
                    YourValue = string.IsNullOrWhiteSpace(currentValues.Name) ? "-" : currentValues.Name.Trim(),
                    LatestValue = string.IsNullOrWhiteSpace(latestValues.Name) ? "-" : latestValues.Name.Trim()
                },
                new()
                {
                    FieldLabel = "Type",
                    YourValue = currentValues.Type.ToString(),
                    LatestValue = latestValues.Type.ToString()
                },
                new()
                {
                    FieldLabel = "Icon",
                    YourValue = CategoryVisualCatalog.GetIconLabel(
                        CategoryVisualCatalog.ResolveIconKeyOrDefault(currentValues.IconKey)),
                    LatestValue = CategoryVisualCatalog.GetIconLabel(
                        CategoryVisualCatalog.ResolveIconKeyOrDefault(latestValues.IconKey))
                },
                new()
                {
                    FieldLabel = "Color",
                    YourValue = CategoryVisualCatalog.GetColorLabel(
                        CategoryVisualCatalog.ResolveColorKeyOrDefault(currentValues.ColorKey)),
                    LatestValue = CategoryVisualCatalog.GetColorLabel(
                        CategoryVisualCatalog.ResolveColorKeyOrDefault(latestValues.ColorKey))
                }
            };
        }

        private static CategoryConflictSnapshot ToConflictSnapshot(CategoryUpsertViewModel model)
        {
            return new CategoryConflictSnapshot
            {
                RowVersionHex = model.RowVersion ?? string.Empty,
                Name = model.Name,
                Type = model.Type,
                IconKey = CategoryVisualCatalog.ResolveIconKeyOrDefault(model.IconKey),
                ColorKey = CategoryVisualCatalog.ResolveColorKeyOrDefault(model.ColorKey)
            };
        }

        private static CategoryConflictSnapshot ToConflictSnapshot(Category category)
        {
            return new CategoryConflictSnapshot
            {
                RowVersionHex = category.RowVersion != null && category.RowVersion.Length > 0
                    ? Convert.ToHexString(category.RowVersion)
                    : string.Empty,
                Name = category.Name,
                Type = category.Type,
                IconKey = CategoryVisualCatalog.ResolveIconKeyOrDefault(category.IconKey),
                ColorKey = CategoryVisualCatalog.ResolveColorKeyOrDefault(category.ColorKey)
            };
        }

        private static bool TryDecodeRowVersion(string? encodedRowVersion, out byte[] rowVersion)
        {
            rowVersion = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(encodedRowVersion))
            {
                return false;
            }

            try
            {
                rowVersion = Convert.FromHexString(encodedRowVersion.Trim());
                return rowVersion.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

    }
}
