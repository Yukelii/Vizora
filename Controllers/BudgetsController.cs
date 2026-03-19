using System.Globalization;
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

            if (!TryMapToRequest(model, out var request))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the record version. Please reload and try again.");
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

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
                RowVersion = Convert.ToHexString(budget.RowVersion),
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

            if (!TryMapToRequest(model, out var request))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the record version. Please reload and try again.");
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            try
            {
                var updateResult = await _budgetService.UpdateAsync(id, request, model.ForceOverwrite);
                if (updateResult.Status == UpdateOperationStatus.NotFound)
                {
                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update budget.");
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status == UpdateOperationStatus.Conflict && updateResult.Conflict != null)
                {
                    if (IsModalRequest())
                    {
                        var categories = await _categoryService.GetAllAsync();
                        return PartialView(
                            "~/Views/Shared/_Modal.cshtml",
                            new Dictionary<string, object?>
                            {
                                ["Size"] = "md",
                                ["Variant"] = "details",
                                ["BodyPartial"] = "~/Views/Shared/_ConcurrencyModal.cshtml",
                                ["BodyModel"] = BuildBudgetConflictModalModel(id, updateResult.Conflict, categories)
                            });
                    }

                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    ModelState.AddModelError(
                        string.Empty,
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.");
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    ModelState.AddModelError(string.Empty, "Unable to update budget.");
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
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

        private static bool TryMapToRequest(BudgetUpsertViewModel model, out BudgetUpsertRequest request)
        {
            request = new BudgetUpsertRequest
            {
                RowVersion = Array.Empty<byte>(),
                CategoryId = model.CategoryId,
                PlannedAmount = model.PlannedAmount,
                PeriodType = model.PeriodType,
                StartDate = model.StartDate,
                EndDate = model.EndDate
            };

            if (string.IsNullOrWhiteSpace(model.RowVersion))
            {
                return true;
            }

            try
            {
                request.RowVersion = Convert.FromHexString(model.RowVersion.Trim());
                return request.RowVersion.Length > 0;
            }
            catch (FormatException)
            {
                request.RowVersion = Array.Empty<byte>();
                return false;
            }
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

        private bool IsModalRequest()
        {
            return string.Equals(
                Request.Headers["X-Requested-With"],
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);
        }

        private ConcurrencyModalViewModel BuildBudgetConflictModalModel(
            int id,
            ConcurrencyConflictResult<BudgetConflictSnapshot> conflict,
            IReadOnlyList<Category> categories)
        {
            var current = conflict.CurrentValues;
            var latest = conflict.DatabaseValues;
            var categoryLookup = categories.ToDictionary(c => c.Id, c => c.Name);

            var model = new ConcurrencyModalViewModel
            {
                EntityName = "budget",
                ReloadUrl = Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                ReloadModalTitle = "Edit Budget",
                OverwriteActionUrl = Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                OverwriteButtonLabel = "Overwrite"
            };

            model.FieldComparisons = new List<ConcurrencyFieldComparisonViewModel>
            {
                new()
                {
                    FieldLabel = "Category",
                    YourValue = categoryLookup.GetValueOrDefault(current.CategoryId, "Unknown"),
                    LatestValue = categoryLookup.GetValueOrDefault(latest.CategoryId, "Unknown")
                },
                new()
                {
                    FieldLabel = "Planned Amount",
                    YourValue = current.PlannedAmount.ToString("N2", CultureInfo.InvariantCulture),
                    LatestValue = latest.PlannedAmount.ToString("N2", CultureInfo.InvariantCulture)
                },
                new()
                {
                    FieldLabel = "Period Type",
                    YourValue = current.PeriodType.ToString(),
                    LatestValue = latest.PeriodType.ToString()
                },
                new()
                {
                    FieldLabel = "Start Date",
                    YourValue = current.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LatestValue = latest.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                new()
                {
                    FieldLabel = "End Date",
                    YourValue = current.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LatestValue = latest.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }
            };

            model.HiddenFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Id"] = id.ToString(CultureInfo.InvariantCulture),
                ["RowVersion"] = latest.RowVersionHex,
                ["ForceOverwrite"] = bool.TrueString,
                ["CategoryId"] = current.CategoryId.ToString(CultureInfo.InvariantCulture),
                ["PlannedAmount"] = current.PlannedAmount.ToString(CultureInfo.InvariantCulture),
                ["PeriodType"] = ((int)current.PeriodType).ToString(CultureInfo.InvariantCulture),
                ["StartDate"] = current.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["EndDate"] = current.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };

            return model;
        }
    }
}
