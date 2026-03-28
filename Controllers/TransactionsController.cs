using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
            var transactionRows = pagedTransactions.Items
                .Select(TransactionListItemViewModel.FromTransaction)
                .ToList();

            var model = new TransactionsIndexViewModel
            {
                Transactions = transactionRows,
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
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "transaction", model.Id, ModalFailureType.Validation);
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
                return this.IsModalRequest()
                    ? this.ModalSuccess()
                    : RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                // Service throws for user-scope and category validation failures.
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "transaction", model.Id, ModalFailureType.Validation);
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "transaction", model.Id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to create transaction due to an unexpected error. Please try again.");
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
            this.LogModalLifecycle(_logger, "transaction", id, "edit_form_loaded");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, TransactionUpsertViewModel model)
        {
            this.LogModalLifecycle(_logger, "transaction", id, "edit_submit_attempted");

            if (!model.Id.HasValue || id != model.Id.Value)
            {
                if (this.IsModalRequest())
                {
                    this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Validation, renderedInModalResponse: true);
                    return this.ModalError(
                        "Unable to update transaction because the request did not match the selected record.",
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
                var latestTransaction = await _transactionService.GetByIdAsync(id);
                if (latestTransaction == null)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error, renderedInModalResponse: true);
                        return this.ModalError(
                            "This transaction no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Validation, renderedInModalResponse: true);
                ModelState.AddModelError(
                    string.Empty,
                    invalidEditStateMessage);
                model.RowVersion = Convert.ToHexString(latestTransaction.RowVersion);
                model.ForceOverwrite = false;
                ModelState.Remove(nameof(model.RowVersion));
                ModelState.Remove(nameof(model.ForceOverwrite));
                this.ClearModalConflictBanner();
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }

            // Keep form state intact when validation fails.
            if (!ModelState.IsValid)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Validation, renderedInModalResponse: true);
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
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error, renderedInModalResponse: true);
                        return this.ModalError(
                            "This transaction no longer exists or you no longer have access to it.",
                            statusCode: StatusCodes.Status404NotFound,
                            outcome: "not_found");
                    }

                    return NotFound();
                }

                if (updateResult.Status == UpdateOperationStatus.ValidationFailed)
                {
                    this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                    this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Validation, renderedInModalResponse: true);
                    ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Unable to update transaction.");
                    await PopulateCategoryDropdownAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status == UpdateOperationStatus.Conflict && updateResult.Conflict != null)
                {
                    var categoryLookup = await BuildCategoryLookupAsync();
                    this.SetModalStateAndStatus(ModalUiState.Conflict, StatusCodes.Status409Conflict);
                    this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Concurrency, renderedInModalResponse: true);
                    model.RowVersion = updateResult.Conflict.DatabaseValues.RowVersionHex;
                    model.ForceOverwrite = false;
                    ModelState.Remove(nameof(model.RowVersion));
                    ModelState.Remove(nameof(model.ForceOverwrite));
                    this.SetModalConflictBanner(
                        updateResult.ErrorMessage ?? "This record was modified while you were editing.",
                        Url.Action(nameof(Edit), new { id }) ?? string.Empty,
                        "Edit Transaction",
                        BuildTransactionConflictComparisons(
                            updateResult.Conflict.CurrentValues,
                            updateResult.Conflict.DatabaseValues,
                            categoryLookup),
                        allowOverwrite: true);
                    await PopulateCategoryDropdownAsync(model.CategoryId);
                    return View(model);
                }

                if (updateResult.Status != UpdateOperationStatus.Success)
                {
                    this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                    this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error, renderedInModalResponse: true);
                    ModelState.AddModelError(string.Empty, "Unable to update transaction.");
                    await PopulateCategoryDropdownAsync(model.CategoryId);
                    return View(model);
                }

                if (this.IsModalRequest())
                {
                    this.LogModalLifecycle(_logger, "transaction", id, "edit_submit_succeeded");
                    return this.ModalSuccess("Transaction updated successfully.");
                }

                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                this.SetModalStateAndStatus(ModalUiState.ValidationError, StatusCodes.Status400BadRequest);
                this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Validation, renderedInModalResponse: true);
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateCategoryDropdownAsync(model.CategoryId);
                return View(model);
            }
            catch (Exception ex)
            {
                this.SetModalStateAndStatus(ModalUiState.Error, StatusCodes.Status500InternalServerError);
                this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error, ex, renderedInModalResponse: true);
                ModelState.AddModelError(string.Empty, "Unable to update transaction due to an unexpected error. Please try again.");
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
            try
            {
                var deleted = await _transactionService.DeleteAsync(id);
                if (!deleted)
                {
                    if (this.IsModalRequest())
                    {
                        this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error);
                        return this.ModalError("This transaction no longer exists or you no longer have access to it.");
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
                this.LogModalFailure(_logger, "transaction", id, ModalFailureType.Error, ex);
                ModelState.AddModelError(string.Empty, "Unable to delete transaction due to an unexpected error. Please try again.");

                var transaction = await _transactionService.GetByIdAsync(id);
                if (transaction == null)
                {
                    return this.IsModalRequest()
                        ? this.ModalError("Unable to load this transaction after the failed delete request.")
                        : NotFound();
                }

                return View("Delete", transaction);
            }
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

        private async Task<IReadOnlyDictionary<int, string>> BuildCategoryLookupAsync()
        {
            var categories = await _categoryService.GetAllAsync();
            return categories.ToDictionary(
                c => c.Id,
                c => $"{c.Name} ({c.Type})");
        }

        private static IList<ConcurrencyFieldComparisonViewModel> BuildTransactionConflictComparisons(
            TransactionConflictSnapshot currentValues,
            TransactionConflictSnapshot latestValues,
            IReadOnlyDictionary<int, string> categoryLookup)
        {
            return new List<ConcurrencyFieldComparisonViewModel>
            {
                new()
                {
                    FieldLabel = "Date",
                    YourValue = currentValues.TransactionDate.Date.ToString("yyyy-MM-dd"),
                    LatestValue = latestValues.TransactionDate.Date.ToString("yyyy-MM-dd")
                },
                new()
                {
                    FieldLabel = "Category",
                    YourValue = ResolveCategoryLabel(currentValues.CategoryId, categoryLookup),
                    LatestValue = ResolveCategoryLabel(latestValues.CategoryId, categoryLookup)
                },
                new()
                {
                    FieldLabel = "Amount",
                    YourValue = currentValues.Amount.ToString("0.00"),
                    LatestValue = latestValues.Amount.ToString("0.00")
                },
                new()
                {
                    FieldLabel = "Description",
                    YourValue = string.IsNullOrWhiteSpace(currentValues.Description) ? "-" : currentValues.Description.Trim(),
                    LatestValue = string.IsNullOrWhiteSpace(latestValues.Description) ? "-" : latestValues.Description.Trim()
                }
            };
        }

        private static TransactionConflictSnapshot ToConflictSnapshot(TransactionUpsertViewModel model)
        {
            return new TransactionConflictSnapshot
            {
                RowVersionHex = model.RowVersion ?? string.Empty,
                CategoryId = model.CategoryId,
                Amount = Math.Round(model.Amount, 2),
                Description = model.Description,
                TransactionDate = model.TransactionDate
            };
        }

        private static TransactionConflictSnapshot ToConflictSnapshot(Transaction transaction)
        {
            return new TransactionConflictSnapshot
            {
                RowVersionHex = transaction.RowVersion != null && transaction.RowVersion.Length > 0
                    ? Convert.ToHexString(transaction.RowVersion)
                    : string.Empty,
                CategoryId = transaction.CategoryId,
                Amount = Math.Round(transaction.Amount, 2),
                Description = transaction.Description,
                TransactionDate = transaction.TransactionDate
            };
        }

        private static string ResolveCategoryLabel(int categoryId, IReadOnlyDictionary<int, string> categoryLookup)
        {
            return categoryLookup.TryGetValue(categoryId, out var categoryName)
                ? categoryName
                : $"Category #{categoryId}";
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
