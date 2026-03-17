using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.DTOs;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public interface ITransactionReportService
    {
        Task<TransactionReportExportResultDto> ExportTransactionsCsvAsync(TransactionReportExportRequestDto? request = null);
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

        public async Task<TransactionReportExportResultDto> ExportTransactionsCsvAsync(TransactionReportExportRequestDto? request = null)
        {
            var result = new TransactionReportExportResultDto
            {
                FileName = $"vizora-transactions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"
            };

            request ??= new TransactionReportExportRequestDto();

            try
            {
                if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate.Value.Date > request.EndDate.Value.Date)
                {
                    return FinalizeResult(
                        result,
                        OperationOutcomeStatus.InvalidRequest,
                        "Start date cannot be later than end date.",
                        issueCode: "REPORT_INVALID_DATE_RANGE");
                }

                var userId = _userContextService.GetRequiredUserId();

                IQueryable<Transaction> query = _context.Transactions
                    .AsNoTracking()
                    .Include(t => t.Category)
                    .Where(t => t.UserId == userId);

                if (request.StartDate.HasValue)
                {
                    var start = DateTime.SpecifyKind(request.StartDate.Value.Date, DateTimeKind.Utc);
                    query = query.Where(t => t.TransactionDate >= start);
                }

                if (request.EndDate.HasValue)
                {
                    var end = DateTime.SpecifyKind(request.EndDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                    query = query.Where(t => t.TransactionDate <= end);
                }

                if (request.CategoryId.HasValue)
                {
                    query = query.Where(t => t.CategoryId == request.CategoryId.Value);
                }

                if (request.Type.HasValue)
                {
                    query = query.Where(t => t.Type == request.Type.Value);
                }

                var rows = await query
                    .OrderByDescending(t => t.TransactionDate)
                    .ThenByDescending(t => t.CreatedAt)
                    .ToListAsync();

                result.RowCount = rows.Count;
                result.Content = BuildCsv(rows);

                if (rows.Count == 0)
                {
                    result.Status = OperationOutcomeStatus.Empty;
                    result.UserMessage = "No transactions matched your export filters.";
                }
                else
                {
                    result.Status = OperationOutcomeStatus.Success;
                    result.UserMessage = "Transaction export is ready.";
                }

                await TryLogAuditAsync(new AuditLogRequest
                {
                    EventType = "EXPORT",
                    EntityType = "TransactionReport",
                    EntityId = "transactions-csv",
                    NewValues = new
                    {
                        result.Status,
                        result.RowCount,
                        request.StartDate,
                        request.EndDate,
                        request.CategoryId,
                        request.Type
                    }
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected transaction report export failure.");
                return FinalizeResult(
                    result,
                    OperationOutcomeStatus.Failed,
                    "Unable to generate export due to an unexpected error. Please try again.",
                    issueCode: "REPORT_UNEXPECTED",
                    isDataTrusted: false);
            }
        }

        private static byte[] BuildCsv(IEnumerable<Transaction> rows)
        {
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

            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        private static TransactionReportExportResultDto FinalizeResult(
            TransactionReportExportResultDto result,
            OperationOutcomeStatus status,
            string userMessage,
            string? issueCode = null,
            bool isDataTrusted = true)
        {
            result.Status = status;
            result.UserMessage = userMessage;
            result.IsDataTrusted = isDataTrusted;

            if (!string.IsNullOrWhiteSpace(issueCode))
            {
                result.Issues.Add(new OperationIssueDto
                {
                    Code = issueCode,
                    Message = userMessage
                });
            }

            return result;
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
