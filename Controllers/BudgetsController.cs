using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class BudgetsController : Controller
    {
        private readonly IBudgetService _budgetService;
        private readonly ICategoryService _categoryService;

        public BudgetsController(IBudgetService budgetService, ICategoryService categoryService)
        {
            _budgetService = budgetService;
            _categoryService = categoryService;
        }

        public async Task<IActionResult> Index()
        {
            var budgets = await _budgetService.GetAllWithPerformanceAsync();
            return View(budgets);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var budget = await _budgetService.GetPerformanceByIdAsync(id.Value);
            if (budget == null)
            {
                return NotFound();
            }

            return View(budget);
        }

        public async Task<IActionResult> Create()
        {
            var nowUtc = DateTime.UtcNow;
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var model = new BudgetUpsertViewModel
            {
                PlannedAmount = 0m,
                PeriodType = BudgetPeriodType.Monthly,
                StartDate = monthStart.Date,
                EndDate = monthEnd.Date
            };

            await PopulateExpenseCategoriesAsync(null);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BudgetUpsertViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            var request = MapToRequest(model);

            try
            {
                await _budgetService.CreateAsync(request);
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var budget = await _budgetService.GetByIdAsync(id.Value);
            if (budget == null || budget.BudgetPeriod == null)
            {
                return NotFound();
            }

            var model = new BudgetUpsertViewModel
            {
                Id = budget.Id,
                CategoryId = budget.CategoryId,
                PlannedAmount = budget.PlannedAmount,
                PeriodType = budget.BudgetPeriod.Type,
                StartDate = budget.BudgetPeriod.StartDate.Date,
                EndDate = budget.BudgetPeriod.EndDate.Date
            };

            await PopulateExpenseCategoriesAsync(model.CategoryId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BudgetUpsertViewModel model)
        {
            if (!model.Id.HasValue || id != model.Id.Value)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            var request = MapToRequest(model);

            try
            {
                var updated = await _budgetService.UpdateAsync(id, request);
                if (!updated)
                {
                    return NotFound();
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var budget = await _budgetService.GetPerformanceByIdAsync(id.Value);
            if (budget == null)
            {
                return NotFound();
            }

            return View(budget);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deleted = await _budgetService.DeleteAsync(id);
            if (!deleted)
            {
                return NotFound();
            }

            return RedirectToAction(nameof(Index));
        }

        private static BudgetUpsertRequest MapToRequest(BudgetUpsertViewModel model)
        {
            return new BudgetUpsertRequest
            {
                CategoryId = model.CategoryId,
                PlannedAmount = model.PlannedAmount,
                PeriodType = model.PeriodType,
                StartDate = model.StartDate,
                EndDate = model.EndDate
            };
        }

        private async Task PopulateExpenseCategoriesAsync(int? selectedCategoryId)
        {
            var categories = await _categoryService.GetAllAsync();
            var expenseCategories = categories
                .Where(c => c.Type == TransactionType.Expense)
                .OrderBy(c => c.Name)
                .ToList();

            if (!expenseCategories.Any())
            {
                ModelState.AddModelError(string.Empty, "Create at least one expense category before adding a budget.");
            }

            ViewData["CategoryId"] = new SelectList(expenseCategories, "Id", "Name", selectedCategoryId);
        }
    }
}
