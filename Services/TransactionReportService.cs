using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public interface ITransactionReportService
    {
        Task<byte[]> ExportTransactionsCsvAsync();
    }

    public class TransactionReportService : ITransactionReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TransactionReportService> _logger;

        public TransactionReportService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<TransactionReportService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<byte[]> ExportTransactionsCsvAsync()
        {
            var userId = _userContextService.GetRequiredUserId();

            var rows = await _context.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("TransactionId,TransactionDate,Type,Category,Amount,Description");

            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",",
                    row.Id,
                    row.TransactionDate.ToString("yyyy-MM-dd"),
                    row.Type,
                    CsvExportSecurityHelper.SanitizeAndEscape(row.Category?.Name ?? "Uncategorized"),
                    row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvExportSecurityHelper.SanitizeAndEscape(row.Description ?? string.Empty)));
            }

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "EXPORT",
                EntityType = "TransactionReport",
                EntityId = "transactions-csv",
                NewValues = new
                {
                    RowCount = rows.Count
                }
            });

            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        private async Task TryLogAuditAsync(AuditLogRequest request)
        {
            try
            {
                await _auditService.LogAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Audit logging failed in transaction report flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }
    }
}
