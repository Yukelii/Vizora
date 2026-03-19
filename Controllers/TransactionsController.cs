using System.Globalization;
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
                RowVersion = Convert.ToHexString(transaction.RowVersion),
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

            if (!TryDecodeRowVersion(model.RowVersion, out var rowVersion))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to process the record version. Please reload and try again.");
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }

            var transaction = new Transaction
            {
                Id = id,
                RowVersion = rowVersion,
                CategoryId = model.CategoryId,
                Amount = model.Amount,
                Description = model.Description,
                TransactionDate = model.TransactionDate
            };

            try
            {
                var updateResult = await _transactionService.UpdateAsync(transaction, model.ForceOverwrite);
                if (updateResult.Status == UpdateOperationStatus.NotFound)
                {
                    // Not found is returned when the record is absent or not owned by the user.
                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update transaction.");
                    await PopulateCategoryDropdownAsync(model.CategoryId);
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
                                ["BodyModel"] = BuildTransactionConflictModalModel(id, updateResult.Conflict, categories)
                            });
                    }

                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    ModelState.AddModelError(
                        string.Empty,
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.");
                    await PopulateCategoryDropdownAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    ModelState.AddModelError(string.Empty, "Unable to update transaction.");
                    await PopulateCategoryDropdownAsync(model.CategoryId);
                    return View(model);
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

        private ConcurrencyModalViewModel BuildTransactionConflictModalModel(
            int id,
            ConcurrencyConflictResult<TransactionConflictSnapshot> conflict,
            IReadOnlyList<Category> categories)
        {
            var current = conflict.CurrentValues;
            var latest = conflict.DatabaseValues;
            var categoryLookup = categories.ToDictionary(c => c.Id, c => c.Name);

            var model = new ConcurrencyModalViewModel
            {
                EntityName = "transaction",
                ReloadUrl = Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                ReloadModalTitle = "Edit Transaction",
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
                    FieldLabel = "Amount",
                    YourValue = current.Amount.ToString("N2", CultureInfo.InvariantCulture),
                    LatestValue = latest.Amount.ToString("N2", CultureInfo.InvariantCulture)
                },
                new()
                {
                    FieldLabel = "Description",
                    YourValue = current.Description ?? "-",
                    LatestValue = latest.Description ?? "-"
                },
                new()
                {
                    FieldLabel = "Transaction Date",
                    YourValue = current.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LatestValue = latest.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }
            };

            model.HiddenFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Id"] = id.ToString(CultureInfo.InvariantCulture),
                ["RowVersion"] = latest.RowVersionHex,
                ["ForceOverwrite"] = bool.TrueString,
                ["CategoryId"] = current.CategoryId.ToString(CultureInfo.InvariantCulture),
                ["Amount"] = current.Amount.ToString(CultureInfo.InvariantCulture),
                ["Description"] = current.Description ?? string.Empty,
                ["TransactionDate"] = current.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };

            return model;
        }
    }
}
