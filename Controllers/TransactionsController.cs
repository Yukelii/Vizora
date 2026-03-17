using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class TransactionsController : Controller
    {
        private readonly ITransactionService _transactionService;
        private readonly ICategoryService _categoryService;
        private readonly ITransactionImportService _transactionImportService;
        private readonly ILogger<TransactionsController> _logger;

        public TransactionsController(
            ITransactionService transactionService,
            ICategoryService categoryService,
            ITransactionImportService transactionImportService,
            ILogger<TransactionsController> logger)
        {
            _transactionService = transactionService;
            _categoryService = categoryService;
            _transactionImportService = transactionImportService;
            _logger = logger;
        }

        public async Task<IActionResult> Index([FromQuery] TransactionListQuery query)
        {
            if (query.MinAmount.HasValue && query.MaxAmount.HasValue && query.MinAmount > query.MaxAmount)
            {
                ModelState.AddModelError(string.Empty, "Minimum amount cannot be greater than maximum amount.");
            }

            if (query.StartDate.HasValue && query.EndDate.HasValue && query.StartDate.Value.Date > query.EndDate.Value.Date)
            {
                ModelState.AddModelError(string.Empty, "Start date cannot be later than end date.");
            }

            var pagedTransactions = await _transactionService.GetPagedAsync(query);
            var categories = await _categoryService.GetAllAsync();

            var model = new TransactionsIndexViewModel
            {
                Transactions = pagedTransactions.Items,
                CategoryOptions = categories
                    .Select(c => new CategoryFilterOptionViewModel
                    {
                        Id = c.Id,
                        Name = $"{c.Name} ({c.Type})"
                    })
                    .ToList(),
                Search = query.Search?.Trim(),
                Category = query.Category,
                StartDate = query.StartDate?.Date,
                EndDate = query.EndDate?.Date,
                MinAmount = query.MinAmount,
                MaxAmount = query.MaxAmount,
                Page = pagedTransactions.Page,
                PageSize = pagedTransactions.PageSize,
                TotalCount = pagedTransactions.TotalCount
            };

            return View(model);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var transaction = await _transactionService.GetByIdAsync(id.Value);
            if (transaction == null)
            {
                return NotFound();
            }

            return View(transaction);
        }

        public async Task<IActionResult> Create()
        {
            var model = new TransactionUpsertViewModel
            {
                // Default to current UTC date for first-time entry convenience.
                TransactionDate = DateTime.UtcNow.Date
            };

            await PopulateCategoryDropdownAsync(null);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TransactionUpsertViewModel model)
        {
            // If validation fails, repopulate dependent dropdown state before returning the form.
            if (!ModelState.IsValid)
            {
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }

            var transaction = new Transaction
            {
                CategoryId = model.CategoryId,
                Amount = model.Amount,
                Description = model.Description,
                TransactionDate = model.TransactionDate
            };

            try
            {
                await _transactionService.CreateAsync(transaction);
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Service throws for user-scope and category validation failures.
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var transaction = await _transactionService.GetByIdAsync(id.Value);
            if (transaction == null)
            {
                return NotFound();
            }

            var model = new TransactionUpsertViewModel
            {
                Id = transaction.Id,
                RowVersion = transaction.RowVersion,
                CategoryId = transaction.CategoryId,
                Amount = transaction.Amount,
                Description = transaction.Description,
                TransactionDate = transaction.TransactionDate.Date
            };

            await PopulateCategoryDropdownAsync(model.CategoryId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TransactionUpsertViewModel model)
        {
            if (!model.Id.HasValue || id != model.Id.Value)
            {
                return NotFound();
            }

            // Keep form state intact when validation fails.
            if (!ModelState.IsValid)
            {
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }

            var transaction = new Transaction
            {
                Id = id,
                RowVersion = model.RowVersion,
                CategoryId = model.CategoryId,
                Amount = model.Amount,
                Description = model.Description,
                TransactionDate = model.TransactionDate
            };

            try
            {
                var updated = await _transactionService.UpdateAsync(transaction);
                if (!updated)
                {
                    // Not found is returned when the record is absent or not owned by the user.
                    return NotFound();
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var transaction = await _transactionService.GetByIdAsync(id.Value);
            if (transaction == null)
            {
                return NotFound();
            }

            return View(transaction);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deleted = await _transactionService.DeleteAsync(id);
            if (!deleted)
            {
                return NotFound();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile? csvFile)
        {
            try
            {
                var result = await _transactionImportService.ImportCsvAsync(csvFile!);
                var details = new List<OperationIssueDto>
                {
                    new()
                    {
                        Code = "IMPORT_SUMMARY",
                        Message =
                            $"Imported: {result.ImportedCount} | Duplicates: {result.DuplicateCount} | Rejected: {result.RejectedCount} | Processed: {result.ProcessedCount}"
                    }
                };
                details.AddRange(result.Issues.Take(5));

                OperationFeedbackTempData.Set(TempData, new OperationResultDto
                {
                    Status = result.Status,
                    UserMessage = result.UserMessage,
                    IsDataTrusted = result.IsDataTrusted,
                    Issues = details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected import failure reached controller boundary.");
                OperationFeedbackTempData.Set(TempData, new OperationResultDto
                {
                    Status = OperationOutcomeStatus.Failed,
                    UserMessage = "Import failed due to an unexpected error. Please try again.",
                    IsDataTrusted = false,
                    Issues = new List<OperationIssueDto>
                    {
                        new()
                        {
                            Code = "IMPORT_UNEXPECTED",
                            Message = "Import failed due to an unexpected error. Please try again."
                        }
                    }
                });
            }

            return RedirectToAction("Index", "Reports");
        }

        private async Task PopulateCategoryDropdownAsync(int? selectedCategoryId)
        {
            var categories = await _categoryService.GetAllAsync();
            // Provide both display labels and a lightweight map for UI type hints.
            ViewData["CategoryId"] = new SelectList(categories, "Id", "Name", selectedCategoryId);
            ViewData["CategoryTypeMap"] = categories.ToDictionary(c => c.Id, c => c.Type.ToString());
        }
    }
}
