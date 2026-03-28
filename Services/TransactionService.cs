using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public interface ITransactionService
    {
        Task<IReadOnlyList<Transaction>> GetAllAsync();

        Task<PagedResult<Transaction>> GetPagedAsync(TransactionListQuery query);

        Task<Transaction?> GetByIdAsync(int id);

        Task CreateAsync(Transaction transaction);

        Task<UpdateOperationResult<TransactionConflictSnapshot>> UpdateAsync(Transaction transaction, bool forceOverwrite = false);

        Task<bool> DeleteAsync(int id);
    }

    public class TransactionService : ITransactionService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TransactionService> _logger;

        public TransactionService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<TransactionService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Transaction>> GetAllAsync()
        {
            var userId = _userContextService.GetRequiredUserId();

            // Include category for UI/reporting while still applying strict user ownership filtering.
            return await _context.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<PagedResult<Transaction>> GetPagedAsync(TransactionListQuery query)
        {
            var userId = _userContextService.GetRequiredUserId();
            var safePage = query.Page <= 0 ? 1 : query.Page;
            var safePageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 100);

            IQueryable<Transaction> transactionsQuery = _context.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId);

            if (query.StartDate.HasValue)
            {
                var startDateUtc = NormalizeStartUtc(query.StartDate.Value);
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate >= startDateUtc);
            }

            if (query.EndDate.HasValue)
            {
                var endDateUtc = NormalizeEndUtc(query.EndDate.Value);
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate <= endDateUtc);
            }

            if (query.Category.HasValue)
            {
                transactionsQuery = transactionsQuery.Where(t => t.CategoryId == query.Category.Value);
            }

            if (query.MinAmount.HasValue)
            {
                transactionsQuery = transactionsQuery.Where(t => t.Amount >= query.MinAmount.Value);
            }

            if (query.MaxAmount.HasValue)
            {
                transactionsQuery = transactionsQuery.Where(t => t.Amount <= query.MaxAmount.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                var likePattern = $"%{search}%";
                var hasAmountSearch = decimal.TryParse(search, NumberStyles.Number, CultureInfo.InvariantCulture, out var amountSearch);

                if (hasAmountSearch)
                {
                    transactionsQuery = transactionsQuery.Where(t =>
                        (t.Description != null && EF.Functions.ILike(t.Description, likePattern)) ||
                        (t.Category != null && EF.Functions.ILike(t.Category.Name, likePattern)) ||
                        t.Amount == amountSearch);
                }
                else
                {
                    transactionsQuery = transactionsQuery.Where(t =>
                        (t.Description != null && EF.Functions.ILike(t.Description, likePattern)) ||
                        (t.Category != null && EF.Functions.ILike(t.Category.Name, likePattern)));
                }
            }

            var totalCount = await transactionsQuery.CountAsync();
            var totalPages = totalCount <= 0
                ? 1
                : (int)Math.Ceiling(totalCount / (double)safePageSize);

            if (safePage > totalPages)
            {
                safePage = totalPages;
            }

            var items = await transactionsQuery
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.CreatedAt)
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToListAsync();

            return new PagedResult<Transaction>
            {
                Items = items,
                TotalCount = totalCount,
                Page = safePage,
                PageSize = safePageSize
            };
        }

        public async Task<Transaction?> GetByIdAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            return await _context.Transactions
                .AsNoTracking()
                .Include(t => t.Category)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        }

        public async Task CreateAsync(Transaction transaction)
        {
            var userId = _userContextService.GetRequiredUserId();
            var category = await ValidateCategoryAsync(userId, transaction.CategoryId);

            if (transaction.Amount <= 0m || transaction.Amount > 999_999_999m)
            {
                throw new InvalidOperationException("Amount must be greater than 0 and within supported limits.");
            }

            // Derive transaction type from selected category and normalize persisted values.
            transaction.UserId = userId;
            transaction.CategoryId = category.Id;
            transaction.Type = category.Type;
            transaction.Amount = Math.Round(transaction.Amount, 2);
            transaction.Description = string.IsNullOrWhiteSpace(transaction.Description)
                ? null
                : transaction.Description.Trim();
            transaction.TransactionDate = NormalizeUtc(transaction.TransactionDate);
            transaction.CreatedAt = DateTime.UtcNow;
            transaction.UpdatedAt = transaction.CreatedAt;

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "CREATE",
                EntityType = "Transaction",
                EntityId = transaction.Id.ToString(CultureInfo.InvariantCulture),
                NewValues = BuildTransactionAuditState(transaction)
            });
        }

        public async Task<UpdateOperationResult<TransactionConflictSnapshot>> UpdateAsync(Transaction transaction, bool forceOverwrite = false)
        {
            var userId = _userContextService.GetRequiredUserId();
            const string staleRecordMessage = "This record is out of sync. Reload the latest values and try again.";

            var existing = await _context.Transactions
                .FirstOrDefaultAsync(t => t.Id == transaction.Id && t.UserId == userId);

            if (existing == null)
            {
                return UpdateOperationResult<TransactionConflictSnapshot>.NotFound();
            }

            if (transaction.RowVersion == null || transaction.RowVersion.Length == 0)
            {
                var databaseSnapshot = ToConflictSnapshot(existing);
                return UpdateOperationResult<TransactionConflictSnapshot>.ConflictDetected(
                    new ConcurrencyConflictResult<TransactionConflictSnapshot>
                    {
                        CurrentValues = databaseSnapshot,
                        DatabaseValues = databaseSnapshot
                    },
                    staleRecordMessage);
            }

            var incomingRowVersionHex = Convert.ToHexString(transaction.RowVersion);
            var currentRowVersionHex = Convert.ToHexString(existing.RowVersion);
            if (!transaction.RowVersion.AsSpan().SequenceEqual(existing.RowVersion))
            {
                _logger.LogInformation(
                    "Transaction concurrency token mismatch detected for TransactionId {TransactionId}. IncomingToken={IncomingToken}, CurrentToken={CurrentToken}",
                    transaction.Id,
                    incomingRowVersionHex,
                    currentRowVersionHex);
            }

            _context.Entry(existing).Property(t => t.RowVersion).OriginalValue = transaction.RowVersion;
            object oldValues = BuildTransactionAuditState(existing);

            // Re-validate category ownership and keep type/category consistency.
            var category = await ValidateCategoryAsync(userId, transaction.CategoryId);

            if (transaction.Amount <= 0m || transaction.Amount > 999_999_999m)
            {
                throw new InvalidOperationException("Amount must be greater than 0 and within supported limits.");
            }

            existing.CategoryId = category.Id;
            existing.Amount = Math.Round(transaction.Amount, 2);
            existing.Type = category.Type;
            existing.Description = string.IsNullOrWhiteSpace(transaction.Description)
                ? null
                : transaction.Description.Trim();
            existing.TransactionDate = NormalizeUtc(transaction.TransactionDate);
            existing.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = ex.Entries.SingleOrDefault();
                if (entry?.Entity is not Transaction)
                {
                    _logger.LogWarning(
                        ex,
                        "Transaction update concurrency conflict had no transaction entry for TransactionId {TransactionId}.",
                        transaction.Id);
                    var latestTransaction = await _context.Transactions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == transaction.Id && t.UserId == userId);

                    if (latestTransaction == null)
                    {
                        return UpdateOperationResult<TransactionConflictSnapshot>.NotFound();
                    }

                    var latestSnapshot = ToConflictSnapshot(latestTransaction);
                    return UpdateOperationResult<TransactionConflictSnapshot>.ConflictDetected(
                        new ConcurrencyConflictResult<TransactionConflictSnapshot>
                        {
                            CurrentValues = latestSnapshot,
                            DatabaseValues = latestSnapshot
                        },
                        staleRecordMessage);
                }

                var databaseValues = await entry.GetDatabaseValuesAsync();
                if (databaseValues == null)
                {
                    return UpdateOperationResult<TransactionConflictSnapshot>.NotFound();
                }

                var databaseTransaction = (Transaction)databaseValues.ToObject();
                oldValues = BuildTransactionAuditState(databaseTransaction);

                if (forceOverwrite)
                {
                    entry.OriginalValues.SetValues(databaseValues);
                    existing.CategoryId = category.Id;
                    existing.Amount = Math.Round(transaction.Amount, 2);
                    existing.Type = category.Type;
                    existing.Description = string.IsNullOrWhiteSpace(transaction.Description)
                        ? null
                        : transaction.Description.Trim();
                    existing.TransactionDate = NormalizeUtc(transaction.TransactionDate);
                    existing.UpdatedAt = DateTime.UtcNow;

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException retryEx)
                    {
                        _logger.LogWarning(
                            retryEx,
                            "Transaction overwrite retry also hit concurrency for TransactionId {TransactionId}.",
                            transaction.Id);
                        return UpdateOperationResult<TransactionConflictSnapshot>.ConflictDetected(
                            new ConcurrencyConflictResult<TransactionConflictSnapshot>
                            {
                                CurrentValues = ToConflictSnapshot(existing),
                                DatabaseValues = ToConflictSnapshot(databaseTransaction)
                            },
                            "This record was modified by another user. Please reload and try again.");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Transaction update concurrency conflict for TransactionId {TransactionId}. IncomingToken={IncomingToken}, CurrentToken={CurrentToken}",
                        transaction.Id,
                        incomingRowVersionHex,
                        currentRowVersionHex);

                    return UpdateOperationResult<TransactionConflictSnapshot>.ConflictDetected(
                        new ConcurrencyConflictResult<TransactionConflictSnapshot>
                        {
                            CurrentValues = new TransactionConflictSnapshot
                            {
                                RowVersionHex = incomingRowVersionHex,
                                CategoryId = category.Id,
                                Amount = Math.Round(transaction.Amount, 2),
                                Description = string.IsNullOrWhiteSpace(transaction.Description)
                                    ? null
                                    : transaction.Description.Trim(),
                                TransactionDate = NormalizeUtc(transaction.TransactionDate)
                            },
                            DatabaseValues = ToConflictSnapshot(databaseTransaction)
                        },
                        "This record was modified while you were editing.");
                }
            }

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "UPDATE",
                EntityType = "Transaction",
                EntityId = existing.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues,
                NewValues = BuildTransactionAuditState(existing)
            });

            return UpdateOperationResult<TransactionConflictSnapshot>.Success();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            var transaction = await _context.Transactions
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (transaction == null)
            {
                return false;
            }

            var oldValues = BuildTransactionAuditState(transaction);

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "DELETE",
                EntityType = "Transaction",
                EntityId = transaction.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues
            });

            return true;
        }

        private async Task<Category> ValidateCategoryAsync(string userId, int categoryId)
        {
            // Prevent users from attaching transactions to categories they do not own.
            var category = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId);

            if (category == null)
            {
                throw new InvalidOperationException("Selected category was not found.");
            }

            return category;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            // Normalize all date values to UTC to keep analytics and filtering consistent.
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

        private static DateTime NormalizeStartUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }

        private static DateTime NormalizeEndUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        }

        private static object BuildTransactionAuditState(Transaction transaction)
        {
            return new
            {
                transaction.CategoryId,
                Type = transaction.Type.ToString(),
                Amount = Math.Round(transaction.Amount, 2),
                TransactionDate = transaction.TransactionDate
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
                    "Audit logging failed in transaction flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }
    }
}
