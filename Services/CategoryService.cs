using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.Models;

namespace Vizora.Services
{
    public interface ICategoryService
    {
        Task<IReadOnlyList<Category>> GetAllAsync(CategoryListFilter filter = CategoryListFilter.All);

        Task<Category?> GetByIdAsync(int id);

        Task CreateAsync(Category category);

        Task<bool> UpdateAsync(Category category);

        Task<bool> DeleteAsync(int id);
    }

    public class CategoryService : ICategoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<CategoryService> _logger;

        public CategoryService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<CategoryService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync(CategoryListFilter filter = CategoryListFilter.All)
        {
            var userId = _userContextService.GetRequiredUserId();

            // Every query is strictly user-scoped to prevent cross-user data access.
            IQueryable<Category> categoriesQuery = _context.Categories
                .AsNoTracking()
                .Where(c => c.UserId == userId);

            if (filter == CategoryListFilter.Expense)
            {
                categoriesQuery = categoriesQuery.Where(c => c.Type == TransactionType.Expense);
            }
            else if (filter == CategoryListFilter.Income)
            {
                categoriesQuery = categoriesQuery.Where(c => c.Type == TransactionType.Income);
            }

            return await categoriesQuery
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<Category?> GetByIdAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            return await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        }

        public async Task CreateAsync(Category category)
        {
            var userId = _userContextService.GetRequiredUserId();
            var normalizedName = NormalizeName(category.Name);

            // Enforce unique category name/type per user.
            var alreadyExists = await _context.Categories.AnyAsync(c =>
                c.UserId == userId &&
                c.Type == category.Type &&
                c.Name.ToLower() == normalizedName.ToLower());

            if (alreadyExists)
            {
                throw new InvalidOperationException("A category with this name and type already exists.");
            }

            category.Name = normalizedName;
            category.UserId = userId;
            category.IconKey = NormalizeAndValidateIconKey(category.IconKey);
            category.ColorKey = NormalizeAndValidateColorKey(category.ColorKey);
            category.CreatedAt = DateTime.UtcNow;

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "CREATE",
                EntityType = "Category",
                EntityId = category.Id.ToString(CultureInfo.InvariantCulture),
                NewValues = BuildCategoryAuditState(category)
            });
        }

        public async Task<bool> UpdateAsync(Category category)
        {
            var userId = _userContextService.GetRequiredUserId();
            var normalizedName = NormalizeName(category.Name);

            if (category.RowVersion == null || category.RowVersion.Length == 0)
            {
                throw new InvalidOperationException("This record was modified by another user. Please reload and try again.");
            }

            var existing = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == category.Id && c.UserId == userId);

            if (existing == null)
            {
                return false;
            }

            _context.Entry(existing).Property(c => c.RowVersion).OriginalValue = category.RowVersion;
            var oldValues = BuildCategoryAuditState(existing);

            // Prevent duplicate name/type collisions after updates.
            var duplicate = await _context.Categories.AnyAsync(c =>
                c.UserId == userId &&
                c.Id != category.Id &&
                c.Type == category.Type &&
                c.Name.ToLower() == normalizedName.ToLower());

            if (duplicate)
            {
                throw new InvalidOperationException("A category with this name and type already exists.");
            }

            existing.Name = normalizedName;
            existing.Type = category.Type;
            existing.IconKey = NormalizeAndValidateIconKey(category.IconKey);
            existing.ColorKey = NormalizeAndValidateColorKey(category.ColorKey);

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
                EntityType = "Category",
                EntityId = existing.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues,
                NewValues = BuildCategoryAuditState(existing)
            });

            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var userId = _userContextService.GetRequiredUserId();

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (category == null)
            {
                return false;
            }

            var oldValues = BuildCategoryAuditState(category);

            // Keep transaction history intact by blocking deletion when referenced.
            var hasTransactions = await _context.Transactions.AnyAsync(t =>
                t.UserId == userId && t.CategoryId == id);

            if (hasTransactions)
            {
                throw new InvalidOperationException("Category cannot be deleted because it is used by existing transactions.");
            }

            // Budgets also hold category references and must block deletion to preserve analytics integrity.
            var hasBudgets = await _context.Budgets.AnyAsync(b =>
                b.UserId == userId && b.CategoryId == id);

            if (hasBudgets)
            {
                throw new InvalidOperationException("Category cannot be deleted because it is used by existing budgets.");
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            await TryLogAuditAsync(new AuditLogRequest
            {
                EventType = "DELETE",
                EntityType = "Category",
                EntityId = category.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = oldValues
            });

            return true;
        }

        private static string NormalizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim();
        }

        private static string NormalizeAndValidateIconKey(string? iconKey)
        {
            var normalized = string.IsNullOrWhiteSpace(iconKey)
                ? CategoryVisualCatalog.DefaultIconKey
                : iconKey.Trim().ToLowerInvariant();

            if (!CategoryVisualCatalog.IsValidIconKey(normalized))
            {
                throw new InvalidOperationException("Selected icon is not supported.");
            }

            return normalized;
        }

        private static string NormalizeAndValidateColorKey(string? colorKey)
        {
            var normalized = string.IsNullOrWhiteSpace(colorKey)
                ? CategoryVisualCatalog.DefaultColorKey
                : colorKey.Trim().ToLowerInvariant();

            if (!CategoryVisualCatalog.IsValidColorKey(normalized))
            {
                throw new InvalidOperationException("Selected color is not supported.");
            }

            return normalized;
        }

        private static object BuildCategoryAuditState(Category category)
        {
            return new
            {
                category.Name,
                Type = category.Type.ToString(),
                category.IconKey,
                category.ColorKey
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
                    "Audit logging failed in category flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }
    }
}
