using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class CategoriesController : Controller
    {
        private readonly ICategoryService _categoryService;

        public CategoriesController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
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
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Surface domain-rule violations (for example duplicate category name/type).
                ModelState.AddModelError(string.Empty, ex.Message);
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

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CategoryUpsertViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!TryDecodeRowVersion(model.RowVersion, out var rowVersion))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the record version. Please reload and try again.");
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
                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update category.");
                    return View(model);
                }

                if (updateResult.Status == UpdateOperationStatus.Conflict && updateResult.Conflict != null)
                {
                    if (IsModalRequest())
                    {
                        return PartialView(
                            "~/Views/Shared/_Modal.cshtml",
                            new Dictionary<string, object?>
                            {
                                ["Size"] = "md",
                                ["Variant"] = "details",
                                ["BodyPartial"] = "~/Views/Shared/_ConcurrencyModal.cshtml",
                                ["BodyModel"] = BuildCategoryConflictModalModel(id, updateResult.Conflict)
                            });
                    }

                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    ModelState.AddModelError(
                        string.Empty,
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.");
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    ModelState.AddModelError(string.Empty, "Unable to update category.");
                    return View(model);
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
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
                    return NotFound();
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Preserve user feedback when deletion is blocked by dependency checks.
                ModelState.AddModelError(string.Empty, ex.Message);
                var category = await _categoryService.GetByIdAsync(id);
                if (category == null)
                {
                    return NotFound();
                }

                return View("Delete", category);
            }
            catch (DbUpdateException)
            {
                // Final safety net for FK references missed by pre-delete validation.
                ModelState.AddModelError(
                    string.Empty,
                    "Category cannot be deleted because it is referenced by other records.");

                var category = await _categoryService.GetByIdAsync(id);
                if (category == null)
                {
                    return NotFound();
                }

                return View("Delete", category);
            }
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

        private bool IsModalRequest()
        {
            return string.Equals(
                Request.Headers["X-Requested-With"],
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);
        }

        private ConcurrencyModalViewModel BuildCategoryConflictModalModel(
            int id,
            ConcurrencyConflictResult<CategoryConflictSnapshot> conflict)
        {
            var current = conflict.CurrentValues;
            var latest = conflict.DatabaseValues;

            var model = new ConcurrencyModalViewModel
            {
                EntityName = "category",
                ReloadUrl = Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                ReloadModalTitle = "Edit Category",
                OverwriteActionUrl = Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                OverwriteButtonLabel = "Overwrite"
            };

            model.FieldComparisons = new List<ConcurrencyFieldComparisonViewModel>
            {
                new()
                {
                    FieldLabel = "Name",
                    YourValue = current.Name,
                    LatestValue = latest.Name
                },
                new()
                {
                    FieldLabel = "Type",
                    YourValue = current.Type.ToString(),
                    LatestValue = latest.Type.ToString()
                },
                new()
                {
                    FieldLabel = "Icon",
                    YourValue = CategoryVisualCatalog.GetIconLabel(current.IconKey),
                    LatestValue = CategoryVisualCatalog.GetIconLabel(latest.IconKey)
                },
                new()
                {
                    FieldLabel = "Color",
                    YourValue = CategoryVisualCatalog.GetColorLabel(current.ColorKey),
                    LatestValue = CategoryVisualCatalog.GetColorLabel(latest.ColorKey)
                }
            };

            model.HiddenFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Id"] = id.ToString(),
                ["RowVersion"] = latest.RowVersionHex,
                ["ForceOverwrite"] = bool.TrueString,
                ["Name"] = current.Name,
                ["Type"] = ((int)current.Type).ToString(),
                ["IconKey"] = current.IconKey,
                ["ColorKey"] = current.ColorKey
            };

            return model;
        }
    }
}
