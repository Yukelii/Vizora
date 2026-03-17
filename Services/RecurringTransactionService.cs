using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Enums;
using Vizora.Models;

namespace Vizora.Services
{
    public interface IRecurringTransactionService
    {
        Task<IReadOnlyList<RecurringTransaction>> GetAllAsync();

        Task<RecurringTransaction?> GetByIdAsync(int id);

        Task CreateAsync(RecurringTransaction recurringTransaction);

        Task<bool> UpdateAsync(RecurringTransaction recurringTransaction);

        Task<bool> DisableAsync(int id);

        Task<int> GenerateDueTransactionsAsync(DateTime? runUntilUtc = null);
    }

    public class RecurringTransactionService : IRecurringTransactionService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<RecurringTransactionService> _logger;

        public RecurringTransactionService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<RecurringTransactionService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<RecurringTransaction>> GetAllAsync()
        {
            var userId = _userContextService.GetRequiredUserId();

            return await _context.RecurringTransactions
                .AsNoTracking()
                .Include(rt => rt.Category)
                .Where(rt => rt.UserId == userId)
                .OrderBy(rt => rt.IsActive ? 0 : 1)
                .ThenBy(rt => rt.NextRunDate)
                .ThenBy(rt => rt.Id)
                .ToListAsync();
        }

        public async Task<RecurringTransaction?> GetByIdAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            return await _context.RecurringTransactions
                .AsNoTracking()
                .Include(rt => rt.Category)
                .FirstOrDefaultAsync(rt => rt.Id == id && rt.UserId == userId);
        }

        public async Task CreateAsync(RecurringTransaction recurringTransaction)
        {
            var userId = _userContextService.GetRequiredUserId();
            var category = await ValidateCategoryAsync(userId, recurringTransaction.CategoryId);

            var startDate = NormalizeUtc(recurringTransaction.StartDate);
            var nextRunDate = NormalizeUtc(recurringTransaction.NextRunDate);
            if (nextRunDate < startDate)
            {
                throw new InvalidOperationException("Next run date must be on or after the start date.");
            }

            recurringTransaction.UserId = userId;
            recurringTransaction.CategoryId = category.Id;
            recurringTransaction.Type = category.Type;
            recurringTransaction.Amount = Math.Round(Math.Abs(recurringTransaction.Amount), 2);
            recurringTransaction.Description = NormalizeDescription(recurringTransaction.Description);
            recurringTransaction.StartDate = startDate;
            recurringTransaction.NextRunDate = nextRunDate;
            recurringTransaction.CreatedAt = DateTime.UtcNow;

            _context.RecurringTransactions.Add(recurringTransaction);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "CREATE",
                EntityType = "RecurringTransaction",
                EntityId = recurringTransaction.Id.ToString(CultureInfo.InvariantCulture),
                NewValues = BuildRecurringAuditState(recurringTransaction)
            });
        }

        public async Task<bool> UpdateAsync(RecurringTransaction recurringTransaction)
        {
            var userId = _userContextService.GetRequiredUserId();

            if (recurringTransaction.RowVersion == null || recurringTransaction.RowVersion.Length == 0)
            {
                throw new InvalidOperationException("This record was modified by another user. Please reload and try again.");
            }

            var existing = await _context.RecurringTransactions
                .FirstOrDefaultAsync(rt => rt.Id == recurringTransaction.Id && rt.UserId == userId);

            if (existing == null)
            {
                return false;
            }

            _context.Entry(existing).Property(rt => rt.RowVersion).OriginalValue = recurringTransaction.RowVersion;
            var category = await ValidateCategoryAsync(userId, recurringTransaction.CategoryId);
            var startDate = NormalizeUtc(recurringTransaction.StartDate);
            var nextRunDate = NormalizeUtc(recurringTransaction.NextRunDate);
            if (nextRunDate < startDate)
            {
                throw new InvalidOperationException("Next run date must be on or after the start date.");
            }

            var oldValues = BuildRecurringAuditState(existing);

            existing.CategoryId = category.Id;
            existing.Type = category.Type;
            existing.Amount = Math.Round(Math.Abs(recurringTransaction.Amount), 2);
            existing.Description = NormalizeDescription(recurringTransaction.Description);
            existing.Frequency = recurringTransaction.Frequency;
            existing.StartDate = startDate;
            existing.NextRunDate = nextRunDate;
            existing.IsActive = recurringTransaction.IsActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new InvalidOperationException(
                    "This record was modified by another user. Please reload and try again.",
                    ex);
            }

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "UPDATE",
                EntityType = "RecurringTransaction",
                EntityId = existing.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues,
                NewValues = BuildRecurringAuditState(existing)
            });

            return true;
        }

        public async Task<bool> DisableAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();
            var recurringTransaction = await _context.RecurringTransactions
                .FirstOrDefaultAsync(rt => rt.Id == id && rt.UserId == userId);

            if (recurringTransaction == null)
            {
                return false;
            }

            if (!recurringTransaction.IsActive)
            {
                return true;
            }

            var oldValues = BuildRecurringAuditState(recurringTransaction);

            recurringTransaction.IsActive = false;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new InvalidOperationException(
                    "This record was modified by another user. Please reload and try again.",
                    ex);
            }

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "DISABLE",
                EntityType = "RecurringTransaction",
                EntityId = recurringTransaction.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues,
                NewValues = BuildRecurringAuditState(recurringTransaction)
            });

            return true;
        }

        public async Task<int> GenerateDueTransactionsAsync(DateTime? runUntilUtc = null)
        {
            var userId = _userContextService.GetRequiredUserId();
            var runUntil = runUntilUtc.HasValue
                ? NormalizeUtc(runUntilUtc.Value)
                : DateTime.UtcNow;

            // Generate for due recurring entries only, scoped to current authenticated user.
            var dueRecurringTransactions = await _context.RecurringTransactions
                .Include(rt => rt.Category)
                .Where(rt =>
                    rt.UserId == userId &&
                    rt.IsActive &&
                    rt.NextRunDate <= runUntil &&
                    rt.Category != null &&
                    rt.Category.UserId == userId)
                .OrderBy(rt => rt.NextRunDate)
                .ToListAsync();

            var generatedCount = 0;
            foreach (var recurring in dueRecurringTransactions)
            {
                var nextRun = NormalizeUtc(recurring.NextRunDate);
                var generatedType = recurring.Category?.Type ?? recurring.Type;
                recurring.Type = generatedType;
                while (nextRun <= runUntil)
                {
                    var generatedTransaction = new Transaction
                    {
                        UserId = userId,
                        CategoryId = recurring.CategoryId,
                        Type = generatedType,
                        Amount = Math.Round(Math.Abs(recurring.Amount), 2),
                        Description = NormalizeDescription(recurring.Description),
                        TransactionDate = nextRun,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.Transactions.Add(generatedTransaction);
                    generatedCount++;
                    nextRun = GetNextRunDate(nextRun, recurring.Frequency);
                }

                // Advance next run to prevent duplicate generation for already-produced periods.
                recurring.NextRunDate = nextRun;
            }

            if (generatedCount > 0)
            {
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    throw new InvalidOperationException(
                        "One or more recurring transactions were modified by another user. Please reload and try again.",
                        ex);
                }
            }
            else if (dueRecurringTransactions.Count > 0)
            {
                // Save NextRunDate updates even when no records were generated by current window logic.
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    throw new InvalidOperationException(
                        "One or more recurring transactions were modified by another user. Please reload and try again.",
                        ex);
                }
            }

            return generatedCount;
        }

        private async Task<Category> ValidateCategoryAsync(string userId, int categoryId)
        {
            var category = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId);

            if (category == null)
            {
                throw new InvalidOperationException("Selected category was not found.");
            }

            return category;
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
                    "Audit logging failed in recurring transaction flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }

        private static object BuildRecurringAuditState(RecurringTransaction recurringTransaction)
        {
            return new
            {
                recurringTransaction.CategoryId,
                Type = recurringTransaction.Type.ToString(),
                recurringTransaction.Amount,
                Frequency = recurringTransaction.Frequency.ToString(),
                recurringTransaction.StartDate,
                recurringTransaction.NextRunDate,
                recurringTransaction.IsActive
            };
        }

        private static DateTime GetNextRunDate(DateTime nextRunDate, RecurringFrequency frequency)
        {
            return frequency switch
            {
                RecurringFrequency.Daily => nextRunDate.AddDays(1),
                RecurringFrequency.Weekly => nextRunDate.AddDays(7),
                RecurringFrequency.Monthly => nextRunDate.AddMonths(1),
                RecurringFrequency.Yearly => nextRunDate.AddYears(1),
                _ => throw new InvalidOperationException("Unsupported recurring frequency.")
            };
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string? NormalizeDescription(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }
}
