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
                RowVersion = category.RowVersion,
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

            var category = new Category
            {
                Id = id,
                RowVersion = model.RowVersion,
                Name = model.Name,
                Type = model.Type,
                IconKey = model.IconKey,
                ColorKey = model.ColorKey
            };

            try
            {
                var updated = await _categoryService.UpdateAsync(category);
                if (!updated)
                {
                    // Not found is possible when record belongs to another user or was deleted.
                    return NotFound();
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
    }
}
