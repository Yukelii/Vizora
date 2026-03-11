using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ITransactionReportService _transactionReportService;

        public ReportsController(ITransactionReportService transactionReportService)
        {
            _transactionReportService = transactionReportService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> ExportTransactionsCsv()
        {
            var bytes = await _transactionReportService.ExportTransactionsCsvAsync();
            var fileName = $"vizora-transactions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }
    }
}
