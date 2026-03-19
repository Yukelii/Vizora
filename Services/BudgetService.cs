using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public interface IBudgetService
    {
        Task<IReadOnlyList<BudgetPerformanceViewModel>> GetAllWithPerformanceAsync(
            DateTime? filterStartDate = null,
            DateTime? filterEndDate = null);

        Task<BudgetPerformanceViewModel?> GetPerformanceByIdAsync(int id);

        Task<Budget?> GetByIdAsync(int id);

        Task CreateAsync(BudgetUpsertRequest request);

        Task<UpdateOperationResult<BudgetConflictSnapshot>> UpdateAsync(int id, BudgetUpsertRequest request, bool forceOverwrite = false);

        Task<bool> DeleteAsync(int id);
    }

    public class BudgetService : IBudgetService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<BudgetService> _logger;

        public BudgetService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<BudgetService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<BudgetPerformanceViewModel>> GetAllWithPerformanceAsync(
            DateTime? filterStartDate = null,
            DateTime? filterEndDate = null)
        {
            var userId = _userContextService.GetRequiredUserId();
            var normalizedFilterStart = filterStartDate.HasValue
                ? NormalizeStartUtc(filterStartDate.Value)
                : (DateTime?)null;
            var normalizedFilterEnd = filterEndDate.HasValue
                ? NormalizeEndUtc(filterEndDate.Value)
                : (DateTime?)null;

            // Keep analytics user-scoped and filter to budgets overlapping the selected range.
            var budgetsQuery = _context.Budgets
                .AsNoTracking()
                .Where(b => b.UserId == userId);

            if (normalizedFilterStart.HasValue)
            {
                var filterStart = normalizedFilterStart.Value;
                budgetsQuery = budgetsQuery.Where(b => b.BudgetPeriod != null && b.BudgetPeriod.EndDate >= filterStart);
            }

            if (normalizedFilterEnd.HasValue)
            {
                var filterEnd = normalizedFilterEnd.Value;
                budgetsQuery = budgetsQuery.Where(b => b.BudgetPeriod != null && b.BudgetPeriod.StartDate <= filterEnd);
            }

            var analyticsRows = await budgetsQuery
                .OrderByDescending(b => b.BudgetPeriod!.StartDate)
                .ThenBy(b => b.Category!.Name)
                .Select(b => new
                {
                    BudgetId = b.Id,
                    CategoryId = b.CategoryId,
                    CategoryName = b.Category != null ? b.Category.Name : "Uncategorized",
                    PeriodType = b.BudgetPeriod != null ? b.BudgetPeriod.Type : BudgetPeriodType.Custom,
                    StartDate = b.BudgetPeriod != null ? b.BudgetPeriod.StartDate : DateTime.UtcNow,
                    EndDate = b.BudgetPeriod != null ? b.BudgetPeriod.EndDate : DateTime.UtcNow,
                    PlannedAmount = b.PlannedAmount,
                    ActualSpending = _context.Transactions
                        .Where(t =>
                            t.UserId == userId &&
                            t.Type == TransactionType.Expense &&
                            t.CategoryId == b.CategoryId &&
                            t.TransactionDate >= b.BudgetPeriod!.StartDate &&
                            t.TransactionDate <= b.BudgetPeriod!.EndDate)
                        .Sum(t => (decimal?)t.Amount) ?? 0m
                })
                .ToListAsync();

            return analyticsRows
                .Select(row =>
                {
                    var remainingAmount = row.PlannedAmount - row.ActualSpending;
                    var usagePercent = row.PlannedAmount <= 0
                        ? 0m
                        : Math.Round((row.ActualSpending / row.PlannedAmount) * 100m, 2);

                    return BuildBudgetPerformance(
                        row.BudgetId,
                        row.CategoryId,
                        row.CategoryName,
                        row.PeriodType,
                        row.StartDate,
                        row.EndDate,
                        row.PlannedAmount,
                        row.ActualSpending,
                        remainingAmount,
                        usagePercent);
                })
                .ToList();
        }

        public async Task<BudgetPerformanceViewModel?> GetPerformanceByIdAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            var row = await _context.Budgets
                .AsNoTracking()
                .Where(b => b.Id == id && b.UserId == userId)
                .Select(b => new
                {
                    BudgetId = b.Id,
                    CategoryId = b.CategoryId,
                    CategoryName = b.Category != null ? b.Category.Name : "Uncategorized",
                    PeriodType = b.BudgetPeriod != null ? b.BudgetPeriod.Type : BudgetPeriodType.Custom,
                    StartDate = b.BudgetPeriod != null ? b.BudgetPeriod.StartDate : DateTime.UtcNow,
                    EndDate = b.BudgetPeriod != null ? b.BudgetPeriod.EndDate : DateTime.UtcNow,
                    PlannedAmount = b.PlannedAmount,
                    ActualSpending = _context.Transactions
                        .Where(t =>
                            t.UserId == userId &&
                            t.Type == TransactionType.Expense &&
                            t.CategoryId == b.CategoryId &&
                            t.TransactionDate >= b.BudgetPeriod!.StartDate &&
                            t.TransactionDate <= b.BudgetPeriod!.EndDate)
                        .Sum(t => (decimal?)t.Amount) ?? 0m
                })
                .FirstOrDefaultAsync();

            if (row == null)
            {
                return null;
            }

            var remainingAmount = row.PlannedAmount - row.ActualSpending;
            var usagePercent = row.PlannedAmount <= 0
                ? 0m
                : Math.Round((row.ActualSpending / row.PlannedAmount) * 100m, 2);

            return BuildBudgetPerformance(
                row.BudgetId,
                row.CategoryId,
                row.CategoryName,
                row.PeriodType,
                row.StartDate,
                row.EndDate,
                row.PlannedAmount,
                row.ActualSpending,
                remainingAmount,
                usagePercent);
        }

        public async Task<Budget?> GetByIdAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            return await _context.Budgets
                .AsNoTracking()
                .Include(b => b.Category)
                .Include(b => b.BudgetPeriod)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        }

        public async Task CreateAsync(BudgetUpsertRequest request)
        {
            var userId = _userContextService.GetRequiredUserId();
            var validatedRequest = await ValidateAndNormalizeAsync(userId, request);

            // Prevent duplicate budgets for the same category and exact period.
            var duplicateBudget = await _context.Budgets
                .AnyAsync(b =>
                    b.UserId == userId &&
                    b.CategoryId == validatedRequest.Category.Id &&
                    b.BudgetPeriod != null &&
                    b.BudgetPeriod.Type == validatedRequest.PeriodType &&
                    b.BudgetPeriod.StartDate == validatedRequest.StartDateUtc &&
                    b.BudgetPeriod.EndDate == validatedRequest.EndDateUtc);

            if (duplicateBudget)
            {
                throw new InvalidOperationException("A budget already exists for the selected category and period.");
            }

            var budgetPeriod = await GetOrCreateBudgetPeriodAsync(
                userId,
                validatedRequest.PeriodType,
                validatedRequest.StartDateUtc,
                validatedRequest.EndDateUtc);

            var budget = new Budget
            {
                UserId = userId,
                CategoryId = validatedRequest.Category.Id,
                BudgetPeriod = budgetPeriod,
                PlannedAmount = validatedRequest.PlannedAmount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Budgets.Add(budget);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "CREATE",
                EntityType = "Budget",
                EntityId = budget.Id.ToString(CultureInfo.InvariantCulture),
                NewValues = BuildBudgetAuditState(budget, budget.BudgetPeriod)
            });
        }

        public async Task<UpdateOperationResult<BudgetConflictSnapshot>> UpdateAsync(
            int id,
            BudgetUpsertRequest request,
            bool forceOverwrite = false)
        {
            var userId = _userContextService.GetRequiredUserId();

            if (request.RowVersion == null || request.RowVersion.Length == 0)
            {
                return UpdateOperationResult<BudgetConflictSnapshot>.ValidationFailed(
                    "This record was modified by another user. Please reload and try again.");
            }

            var budget = await _context.Budgets
                .Include(b => b.BudgetPeriod)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

            if (budget == null)
            {
                return UpdateOperationResult<BudgetConflictSnapshot>.NotFound();
            }

            var incomingRowVersionHex = Convert.ToHexString(request.RowVersion);
            var currentRowVersionHex = Convert.ToHexString(budget.RowVersion);
            if (!request.RowVersion.AsSpan().SequenceEqual(budget.RowVersion))
            {
                _logger.LogInformation(
                    "Budget concurrency token mismatch detected for BudgetId {BudgetId}. IncomingToken={IncomingToken}, CurrentToken={CurrentToken}",
                    id,
                    incomingRowVersionHex,
                    currentRowVersionHex);
            }

            _context.Entry(budget).Property(b => b.RowVersion).OriginalValue = request.RowVersion;
            object oldValues = BuildBudgetAuditState(budget, budget.BudgetPeriod);
            var validatedRequest = await ValidateAndNormalizeAsync(userId, request);

            var duplicateBudget = await _context.Budgets
                .AnyAsync(b =>
                    b.UserId == userId &&
                    b.Id != id &&
                    b.CategoryId == validatedRequest.Category.Id &&
                    b.BudgetPeriod != null &&
                    b.BudgetPeriod.Type == validatedRequest.PeriodType &&
                    b.BudgetPeriod.StartDate == validatedRequest.StartDateUtc &&
                    b.BudgetPeriod.EndDate == validatedRequest.EndDateUtc);

            if (duplicateBudget)
            {
                return UpdateOperationResult<BudgetConflictSnapshot>.ValidationFailed(
                    "A budget already exists for the selected category and period.");
            }

            var originalBudgetPeriodId = budget.BudgetPeriodId;

            var updatedBudgetPeriod = await GetOrCreateBudgetPeriodAsync(
                userId,
                validatedRequest.PeriodType,
                validatedRequest.StartDateUtc,
                validatedRequest.EndDateUtc);

            budget.CategoryId = validatedRequest.Category.Id;
            budget.BudgetPeriod = updatedBudgetPeriod;
            budget.PlannedAmount = validatedRequest.PlannedAmount;
            budget.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = ex.Entries.SingleOrDefault();
                if (entry?.Entity is not Budget)
                {
                    _logger.LogWarning(
                        ex,
                        "Budget update concurrency conflict had no budget entry for BudgetId {BudgetId}.",
                        id);
                    return UpdateOperationResult<BudgetConflictSnapshot>.ValidationFailed(
                        "This record was modified by another user. Please reload and try again.");
                }

                var databaseValues = await entry.GetDatabaseValuesAsync();
                if (databaseValues == null)
                {
                    return UpdateOperationResult<BudgetConflictSnapshot>.NotFound();
                }

                var databaseBudget = await _context.Budgets
                    .AsNoTracking()
                    .Include(b => b.BudgetPeriod)
                    .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

                if (databaseBudget == null)
                {
                    return UpdateOperationResult<BudgetConflictSnapshot>.NotFound();
                }

                oldValues = BuildBudgetAuditState(databaseBudget, databaseBudget.BudgetPeriod);

                if (forceOverwrite)
                {
                    entry.OriginalValues.SetValues(databaseValues);
                    budget.CategoryId = validatedRequest.Category.Id;
                    budget.BudgetPeriod = updatedBudgetPeriod;
                    budget.PlannedAmount = validatedRequest.PlannedAmount;
                    budget.UpdatedAt = DateTime.UtcNow;

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException retryEx)
                    {
                        _logger.LogWarning(
                            retryEx,
                            "Budget overwrite retry also hit concurrency for BudgetId {BudgetId}.",
                            id);
                        return UpdateOperationResult<BudgetConflictSnapshot>.ConflictDetected(
                            new ConcurrencyConflictResult<BudgetConflictSnapshot>
                            {
                                CurrentValues = ToConflictSnapshot(
                                    budget,
                                    budget.BudgetPeriod,
                                    request.RowVersion),
                                DatabaseValues = ToConflictSnapshot(
                                    databaseBudget,
                                    databaseBudget.BudgetPeriod)
                            },
                            "This record was modified by another user. Please reload and try again.");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Budget update concurrency conflict for BudgetId {BudgetId}. IncomingToken={IncomingToken}, CurrentToken={CurrentToken}",
                        id,
                        incomingRowVersionHex,
                        currentRowVersionHex);

                    return UpdateOperationResult<BudgetConflictSnapshot>.ConflictDetected(
                        new ConcurrencyConflictResult<BudgetConflictSnapshot>
                        {
                            CurrentValues = new BudgetConflictSnapshot
                            {
                                RowVersionHex = incomingRowVersionHex,
                                CategoryId = validatedRequest.Category.Id,
                                PlannedAmount = validatedRequest.PlannedAmount,
                                PeriodType = validatedRequest.PeriodType,
                                StartDate = validatedRequest.StartDateUtc.Date,
                                EndDate = validatedRequest.EndDateUtc.Date
                            },
                            DatabaseValues = ToConflictSnapshot(
                                databaseBudget,
                                databaseBudget.BudgetPeriod)
                        },
                        "This record was modified while you were editing.");
                }
            }
            await RemoveBudgetPeriodIfUnusedAsync(userId, originalBudgetPeriodId, budget.BudgetPeriodId);

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "UPDATE",
                EntityType = "Budget",
                EntityId = budget.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues,
                NewValues = BuildBudgetAuditState(budget, budget.BudgetPeriod)
            });

            return UpdateOperationResult<BudgetConflictSnapshot>.Success();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();
            var budget = await _context.Budgets
                .Include(b => b.BudgetPeriod)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

            if (budget == null)
            {
                return false;
            }

            var oldValues = BuildBudgetAuditState(budget, budget.BudgetPeriod);
            var removedBudgetPeriodId = budget.BudgetPeriodId;

            _context.Budgets.Remove(budget);
            await _context.SaveChangesAsync();
            await RemoveBudgetPeriodIfUnusedAsync(userId, removedBudgetPeriodId);

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "DELETE",
                EntityType = "Budget",
                EntityId = budget.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues
            });

            return true;
        }

        private async Task<BudgetPeriod> GetOrCreateBudgetPeriodAsync(
            string userId,
            BudgetPeriodType type,
            DateTime startDateUtc,
            DateTime endDateUtc)
        {
            var existingPeriod = await _context.BudgetPeriods
                .FirstOrDefaultAsync(bp =>
                    bp.UserId == userId &&
                    bp.Type == type &&
                    bp.StartDate == startDateUtc &&
                    bp.EndDate == endDateUtc);

            if (existingPeriod != null)
            {
                return existingPeriod;
            }

            var newPeriod = new BudgetPeriod
            {
                UserId = userId,
                Type = type,
                StartDate = startDateUtc,
                EndDate = endDateUtc,
                CreatedAt = DateTime.UtcNow
            };

            _context.BudgetPeriods.Add(newPeriod);
            return newPeriod;
        }

        private async Task RemoveBudgetPeriodIfUnusedAsync(string userId, int budgetPeriodId, int? ignoreBudgetPeriodId = null)
        {
            if (ignoreBudgetPeriodId.HasValue && budgetPeriodId == ignoreBudgetPeriodId.Value)
            {
                return;
            }

            var hasBudgetReferences = await _context.Budgets
                .AnyAsync(b => b.UserId == userId && b.BudgetPeriodId == budgetPeriodId);

            if (hasBudgetReferences)
            {
                return;
            }

            var orphanPeriod = await _context.BudgetPeriods
                .FirstOrDefaultAsync(bp => bp.Id == budgetPeriodId && bp.UserId == userId);

            if (orphanPeriod == null)
            {
                return;
            }

            _context.BudgetPeriods.Remove(orphanPeriod);
            await _context.SaveChangesAsync();
        }

        private async Task<ValidatedBudgetRequest> ValidateAndNormalizeAsync(string userId, BudgetUpsertRequest request)
        {
            if (request.PlannedAmount <= 0m || request.PlannedAmount > 999_999_999m)
            {
                throw new InvalidOperationException("Planned amount must be greater than 0 and within supported limits.");
            }

            var category = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.UserId == userId);

            if (category == null)
            {
                throw new InvalidOperationException("Selected category was not found.");
            }

            // Budgets are for spending plans, so only expense categories are allowed.
            if (category.Type != TransactionType.Expense)
            {
                throw new InvalidOperationException("Budgets can only be created for expense categories.");
            }

            var startDate = request.StartDate.Date;
            var endDate = request.EndDate.Date;
            if (endDate < startDate)
            {
                throw new InvalidOperationException("End date must be on or after start date.");
            }

            switch (request.PeriodType)
            {
                case BudgetPeriodType.Weekly:
                    if ((endDate - startDate).Days != 6)
                    {
                        throw new InvalidOperationException("Weekly budgets must span exactly 7 days.");
                    }

                    break;

                case BudgetPeriodType.Monthly:
                    var expectedMonthStart = new DateTime(startDate.Year, startDate.Month, 1);
                    var expectedMonthEnd = expectedMonthStart.AddMonths(1).AddDays(-1);

                    if (startDate != expectedMonthStart || endDate != expectedMonthEnd)
                    {
                        throw new InvalidOperationException("Monthly budgets must cover an entire calendar month.");
                    }

                    break;

                case BudgetPeriodType.Custom:
                    break;
            }

            return new ValidatedBudgetRequest
            {
                Category = category,
                PlannedAmount = Math.Round(request.PlannedAmount, 2),
                PeriodType = request.PeriodType,
                StartDateUtc = NormalizeStartUtc(startDate),
                EndDateUtc = NormalizeEndUtc(endDate)
            };
        }

        private static DateTime NormalizeStartUtc(DateTime dateTime)
        {
            return DateTime.SpecifyKind(dateTime.Date, DateTimeKind.Utc);
        }

        private static DateTime NormalizeEndUtc(DateTime dateTime)
        {
            return DateTime.SpecifyKind(dateTime.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        }

        private static BudgetPerformanceViewModel BuildBudgetPerformance(
            int budgetId,
            int categoryId,
            string categoryName,
            BudgetPeriodType periodType,
            DateTime startDate,
            DateTime endDate,
            decimal plannedAmount,
            decimal actualSpending,
            decimal remainingAmount,
            decimal usagePercent)
        {
            return new BudgetPerformanceViewModel
            {
                BudgetId = budgetId,
                CategoryId = categoryId,
                CategoryName = categoryName,
                PeriodType = periodType,
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                PlannedAmount = plannedAmount,
                ActualSpending = actualSpending,
                RemainingAmount = remainingAmount,
                UsagePercent = usagePercent
            };
        }

        private static object BuildBudgetAuditState(Budget budget, BudgetPeriod? period)
        {
            return new
            {
                budget.CategoryId,
                budget.BudgetPeriodId,
                budget.PlannedAmount,
                PeriodType = period?.Type.ToString(),
                StartDate = period?.StartDate,
                EndDate = period?.EndDate
            };
        }

        private static BudgetConflictSnapshot ToConflictSnapshot(
            Budget budget,
            BudgetPeriod? period,
            byte[]? overrideRowVersion = null)
        {
            var rowVersion = overrideRowVersion ?? budget.RowVersion;
            return new BudgetConflictSnapshot
            {
                RowVersionHex = rowVersion != null && rowVersion.Length > 0
                    ? Convert.ToHexString(rowVersion)
                    : string.Empty,
                CategoryId = budget.CategoryId,
                PlannedAmount = budget.PlannedAmount,
                PeriodType = period?.Type ?? BudgetPeriodType.Custom,
                StartDate = (period?.StartDate ?? DateTime.UtcNow).Date,
                EndDate = (period?.EndDate ?? DateTime.UtcNow).Date
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
                    "Audit logging failed in budget flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }

        private sealed class ValidatedBudgetRequest
        {
            public Category Category { get; set; } = null!;

            public decimal PlannedAmount { get; set; }

            public BudgetPeriodType PeriodType { get; set; }

            public DateTime StartDateUtc { get; set; }

            public DateTime EndDateUtc { get; set; }
        }
    }
}
