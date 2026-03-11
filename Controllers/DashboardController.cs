using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vizora.Services;

namespace Vizora.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IFinanceAnalyticsService _financeAnalyticsService;

        public DashboardController(IFinanceAnalyticsService financeAnalyticsService)
        {
            _financeAnalyticsService = financeAnalyticsService;
        }

        public async Task<IActionResult> Index(string filter = "all", DateTime? startDate = null, DateTime? endDate = null)
        {
            var dashboardData = await _financeAnalyticsService.GetDashboardStatisticsAsync(filter, startDate, endDate);
            return View(dashboardData);
        }
    }
}
