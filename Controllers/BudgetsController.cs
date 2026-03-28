using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private readonly ILogger<BudgetsController> _logger;

        public BudgetsController(
            IBudgetService budgetService,
            ICategoryService categoryService,
            ILogger<BudgetsController> logger)
        {
            _budgetService = budgetService;
            _categoryService = categoryService;
            _logger = logger;
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
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", model.Id, ModalFailureType.Validation);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            if (!TryMapToRequest(model, out var request))
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", model.Id, ModalFailureType.Validation);
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the record version. Please reload and try again.");
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            try
            {
                await _budgetService.CreateAsync(request);
                return this.IsModalRequest()
                    ? this.ModalSuccess()
                    : RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", model.Id, ModalFailureType.Validation);
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "budget", model.Id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to create budget due to an unexpected error. Please try again.");
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
            this.LogModalLifecycle(_logger, "budget", id, "edit_form_loaded");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BudgetUpsertViewModel model)
        {
            this.LogModalLifecycle(_logger, "budget", id, "edit_submit_attempted");

            if (!model.Id.HasValue || id != model.Id.Value)
            {
                if (this.IsModalRequest())
                {
                    this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                    return this.ModalError(
                        "Unable to update budget because the request did not match the selected record.",
                        statusCode: StatusCodes.Status400BadRequest,
                        outcome: ModalUiState.ValidationError);
                }

                return NotFound();
            }

            // Missing/invalid tokens indicate an invalid edit session, not a true concurrency conflict.
            ModelState.Remove(nameof(model.RowVersion));
            if (!TryDecodeRowVersion(model.RowVersion, out var rowVersion))
            {
                const string invalidEditStateMessage = "This edit state is invalid or expired. Reload the latest values and try again.";
                var latestBudget = await _budgetService.GetByIdAsync(id);
                if (latestBudget == null || latestBudget.BudgetPeriod == null)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error);
                        return this.ModalError(
                            "This budget no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                ModelState.AddModelError(
                    string.Empty,
                    invalidEditStateMessage);
                model.RowVersion = Convert.ToHexString(latestBudget.RowVersion);
                model.ForceOverwrite = false;
                ModelState.Remove(nameof(model.RowVersion));
                ModelState.Remove(nameof(model.ForceOverwrite));
                this.ClearModalConflictBanner();
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            if (!TryMapToRequest(model, out var request))
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the request payload. Please reload and try again.");
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }

            request.RowVersion = rowVersion;

            try
            {
                var updateResult = await _budgetService.UpdateAsync(id, request, model.ForceOverwrite);
                if (updateResult.Status == UpdateOperationStatus.NotFound)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error);
                        return this.ModalError(
                            "This budget no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                    this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update budget.");
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status == UpdateOperationStatus.Conflict && updateResult.Conflict != null)
                {
                    var categoryLookup = await BuildCategoryLookupAsync();
                    this.SetModalStateAndStatus(ModalUiState.Conflict, StatusCodes.Status409Conflict);
                    this.LogModalFailure(_logger, "budget", id, ModalFailureType.Concurrency);
                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    this.SetModalConflictBanner(
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.",
                        Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                        "Edit Budget",
                        BuildBudgetConflictComparisons(
                            updateResult.Conflict.CurrentValues,
                            updateResult.Conflict.DatabaseValues,
                            categoryLookup),
                        allowOverwrite: true);
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                    this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error);
                    ModelState.AddModelError(string.Empty, "Unable to update budget.");
                    await PopulateExpenseCategoriesAsync(model.CategoryId);
                    return View(model);
                }

                if (this.IsModalRequest())
                {
                    this.LogModalLifecycle(_logger, "budget", id, "edit_submit_succeeded");
                    return this.ModalSuccess("Budget updated successfully.");
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Validation);
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateExpenseCategoriesAsync(model.CategoryId);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to update budget due to an unexpected error. Please try again.");
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
            try
            {
                var deleted = await _budgetService.DeleteAsync(id);
                if (!deleted)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error);
                        return this.ModalError("This budget no longer exists or you no longer have access to it.");
                    }

                    return NotFound();
                }

                return this.IsModalRequest()
                    ? this.ModalSuccess()
                    : RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "budget", id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to delete budget due to an unexpected error. Please try again.");

                var budget = await _budgetService.GetPerformanceByIdAsync(id);
                if (budget == null)
                {
                    return this.IsModalRequest()
                        ? this.ModalError("Unable to load this budget after the failed delete request.")
                        : NotFound();
                }

                return View("Delete", budget);
            }
        }

        private async Task<IReadOnlyDictionary<int, string>> BuildCategoryLookupAsync()
        {
            var categories = await _categoryService.GetAllAsync();
            return categories.ToDictionary(c => c.Id, c => c.Name);
        }

        private static IList<ConcurrencyFieldComparisonViewModel> BuildBudgetConflictComparisons(
            BudgetConflictSnapshot currentValues,
            BudgetConflictSnapshot latestValues,
            IReadOnlyDictionary<int, string> categoryLookup)
        {
            return new List<ConcurrencyFieldComparisonViewModel>
            {
                new()
                {
                    FieldLabel = "Category",
                    YourValue = ResolveCategoryLabel(currentValues.CategoryId, categoryLookup),
                    LatestValue = ResolveCategoryLabel(latestValues.CategoryId, categoryLookup)
                },
                new()
                {
                    FieldLabel = "Planned Amount",
                    YourValue = currentValues.PlannedAmount.ToString("0.00"),
                    LatestValue = latestValues.PlannedAmount.ToString("0.00")
                },
                new()
                {
                    FieldLabel = "Period Type",
                    YourValue = currentValues.PeriodType.ToString(),
                    LatestValue = latestValues.PeriodType.ToString()
                },
                new()
                {
                    FieldLabel = "Start Date",
                    YourValue = currentValues.StartDate.Date.ToString("yyyy-MM-dd"),
                    LatestValue = latestValues.StartDate.Date.ToString("yyyy-MM-dd")
                },
                new()
                {
                    FieldLabel = "End Date",
                    YourValue = currentValues.EndDate.Date.ToString("yyyy-MM-dd"),
                    LatestValue = latestValues.EndDate.Date.ToString("yyyy-MM-dd")
                }
            };
        }

        private static BudgetConflictSnapshot ToConflictSnapshot(BudgetUpsertViewModel model)
        {
            return new BudgetConflictSnapshot
            {
                RowVersionHex = model.RowVersion ?? string.Empty,
                CategoryId = model.CategoryId,
                PlannedAmount = Math.Round(model.PlannedAmount, 2),
                PeriodType = model.PeriodType,
                StartDate = model.StartDate.Date,
                EndDate = model.EndDate.Date
            };
        }

        private static BudgetConflictSnapshot ToConflictSnapshot(Budget budget)
        {
            var period = budget.BudgetPeriod;
            return new BudgetConflictSnapshot
            {
                RowVersionHex = budget.RowVersion != null && budget.RowVersion.Length > 0
                    ? Convert.ToHexString(budget.RowVersion)
                    : string.Empty,
                CategoryId = budget.CategoryId,
                PlannedAmount = Math.Round(budget.PlannedAmount, 2),
                PeriodType = period?.Type ?? BudgetPeriodType.Custom,
                StartDate = (period?.StartDate ?? DateTime.UtcNow).Date,
                EndDate = (period?.EndDate ?? DateTime.UtcNow).Date
            };
        }

        private static string ResolveCategoryLabel(int categoryId, IReadOnlyDictionary<int, string> categoryLookup)
        {
            return categoryLookup.TryGetValue(categoryId, out var categoryName)
                ? categoryName
                : $"Category #{categoryId}";
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

            return true;
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
