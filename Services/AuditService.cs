using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public class AuditLogRequest
    {
        public string EventType { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public object? OldValues { get; set; }

        public object? NewValues { get; set; }
    }

    public interface IAuditService
    {
        Task LogAsync(AuditLogRequest request);
    }

    public class AuditService : IAuditService
    {
        private static readonly JsonSerializerOptions AuditSerializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task LogAsync(AuditLogRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EventType))
            {
                _logger.LogWarning("Audit log skipped: missing event type.");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.EntityType))
            {
                _logger.LogWarning("Audit log skipped: missing entity type.");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.EntityId))
            {
                _logger.LogWarning("Audit log skipped: missing entity ID.");
                return;
            }

            string userId;
            try
            {
                userId = _userContextService.GetRequiredUserId();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit log skipped: authenticated user context was unavailable.");
                return;
            }

            var ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;

            var log = new AuditLog
            {
                EventType = request.EventType.Trim().ToUpperInvariant(),
                EntityType = request.EntityType.Trim(),
                EntityId = request.EntityId.Trim(),
                UserId = userId,
                IpAddress = ipAddress,
                OldValues = SerializeValues(request.OldValues),
                NewValues = SerializeValues(request.NewValues),
                Timestamp = DateTime.UtcNow
            };

            try
            {
                _context.AuditLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Audit persistence should never break the primary finance operation.
                try
                {
                    var entry = _context.Entry(log);
                    if (entry.State != EntityState.Detached)
                    {
                        entry.State = EntityState.Detached;
                    }
                }
                catch
                {
                    // Ignore tracker cleanup errors to avoid cascading failures.
                }

                _logger.LogWarning(
                    ex,
                    "Audit log persistence failed for {EventType}/{EntityType}/{EntityId}.",
                    log.EventType,
                    log.EntityType,
                    log.EntityId);
            }
        }

        private static string SerializeValues(object? values)
        {
            return values == null
                ? string.Empty
                : JsonSerializer.Serialize(values, AuditSerializerOptions);
        }
    }
}
