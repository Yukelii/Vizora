using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ITransactionReportService _transactionReportService;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            ITransactionReportService transactionReportService,
            ILogger<ReportsController> logger)
        {
            _transactionReportService = transactionReportService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var model = new ReportsIndexViewModel
            {
                Feedback = OperationFeedbackTempData.Consume(TempData)
            };

            return View(model);
        }

        public async Task<IActionResult> ExportTransactionsCsv([FromQuery] TransactionReportExportRequestDto request)
        {
            try
            {
                var result = await _transactionReportService.ExportTransactionsCsvAsync(request);
                if (result.Status == OperationOutcomeStatus.Success && result.Content is { Length: > 0 })
                {
                    return File(result.Content, result.ContentType, result.FileName);
                }

                OperationFeedbackTempData.Set(TempData, new OperationResultDto
                {
                    Status = result.Status,
                    UserMessage = result.UserMessage,
                    IsDataTrusted = result.IsDataTrusted,
                    Issues = result.Issues
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected report export controller failure.");
                OperationFeedbackTempData.Set(TempData, new OperationResultDto
                {
                    Status = OperationOutcomeStatus.Failed,
                    UserMessage = "Unable to generate export due to an unexpected error. Please try again.",
                    IsDataTrusted = false,
                    Issues = new List<OperationIssueDto>
                    {
                        new()
                        {
                            Code = "REPORT_UNEXPECTED",
                            Message = "Unable to generate export due to an unexpected error. Please try again."
                        }
                    }
                });
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
