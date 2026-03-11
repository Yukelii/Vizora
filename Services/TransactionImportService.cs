using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;

namespace Vizora.Services
{
    public interface ITransactionImportService
    {
        Task<TransactionImportResultDto> ImportCsvAsync(IFormFile file);
    }

    public class TransactionImportService : ITransactionImportService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ImportLocks = new();

        private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB
        private const int MaxRowsPerImport = 10_000;
        private const int MaxErrorMessages = 20;

        private static readonly string[] SupportedDateFormats =
        {
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy"
        };

        private readonly ApplicationDbContext _context;
        private readonly IUserContextService _userContextService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TransactionImportService> _logger;

        public TransactionImportService(
            ApplicationDbContext context,
            IUserContextService userContextService,
            IAuditService auditService,
            ILogger<TransactionImportService> logger)
        {
            _context = context;
            _userContextService = userContextService;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<TransactionImportResultDto> ImportCsvAsync(IFormFile file)
        {
            var userId = _userContextService.GetRequiredUserId();
            var importLock = ImportLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));

            if (!await importLock.WaitAsync(0))
            {
                throw new InvalidOperationException("An import operation is already running for your account. Please wait until it finishes.");
            }

            try
            {
                var result = new TransactionImportResultDto();
                var startedAtUtc = DateTime.UtcNow;
                var processedRows = 0;
                var createdCategories = 0;
                var status = "SUCCESS";
                string? failureReason = null;
                var safeFileName = string.Empty;
                var safeFileSize = 0L;

                try
                {
                    if (file == null || file.Length <= 0)
                    {
                        throw new InvalidOperationException("Please select a non-empty CSV file.");
                    }

                    safeFileName = Path.GetFileName(file.FileName ?? string.Empty);
                    safeFileSize = file.Length;

                    if (file.Length > MaxFileSizeBytes)
                    {
                        throw new InvalidOperationException("CSV file is too large. Maximum allowed size is 2 MB.");
                    }

                    if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Only CSV files are supported.");
                    }

                    var categories = await _context.Categories
                        .Where(c => c.UserId == userId)
                        .ToListAsync();

                    var categoryMap = categories.ToDictionary(
                        c => BuildCategoryKey(c.Name, c.Type),
                        c => c,
                        StringComparer.Ordinal);

                    await EnsureUncategorizedCategoriesAsync(userId, categoryMap);

                    var transactionsToInsert = new List<Transaction>();
                    var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    using var stream = file.OpenReadStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                    var headerLine = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        throw new InvalidOperationException("CSV file is empty.");
                    }

                    var headerMap = BuildHeaderMap(ParseCsvLine(headerLine));
                    ValidateHeaders(headerMap);

                    var rowCount = 0;
                    var lineNumber = 1;
                    while (true)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null)
                        {
                            break;
                        }

                        lineNumber++;

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        rowCount++;
                        processedRows++;
                        if (rowCount > MaxRowsPerImport)
                        {
                            RegisterRowError(result, lineNumber, "Row limit exceeded. Maximum supported rows per import is 10,000.");
                            break;
                        }

                        if (!TryMapRow(ParseCsvLine(line), headerMap, out var row, out var rowError))
                        {
                            RegisterRowError(result, lineNumber, rowError);
                            continue;
                        }

                        var categoryResolution = await ResolveCategoryAsync(userId, row, categoryMap);
                        var resolvedCategory = categoryResolution.Category;
                        if (categoryResolution.CreatedCategory)
                        {
                            createdCategories++;
                        }

                        if (resolvedCategory == null || resolvedCategory.UserId != userId)
                        {
                            RegisterRowError(result, lineNumber, "Category is invalid for the authenticated user.");
                            continue;
                        }

                        var normalizedDate = NormalizeStartUtc(row.Date);
                        var normalizedDescription = NormalizeDescription(row.Description);
                        var duplicateKey = BuildDuplicateKey(normalizedDate, row.Amount, normalizedDescription);

                        if (importedKeys.Contains(duplicateKey))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        if (await IsDuplicateTransactionAsync(userId, normalizedDate, row.Amount, normalizedDescription))
                        {
                            result.SkippedCount++;
                            importedKeys.Add(duplicateKey);
                            continue;
                        }

                        transactionsToInsert.Add(new Transaction
                        {
                            UserId = userId,
                            CategoryId = resolvedCategory.Id,
                            Type = resolvedCategory.Type,
                            Amount = Math.Round(Math.Abs(row.Amount), 2),
                            Description = normalizedDescription,
                            TransactionDate = normalizedDate,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        importedKeys.Add(duplicateKey);
                        result.ImportedCount++;
                    }

                    if (transactionsToInsert.Count > 0)
                    {
                        _context.Transactions.AddRange(transactionsToInsert);
                        await _context.SaveChangesAsync();
                    }

                    return result;
                }
                catch (InvalidOperationException ex)
                {
                    status = "FAILED";
                    failureReason = ex.Message;
                    throw;
                }
                finally
                {
                    await TryLogAuditAsync(new AuditLogRequest
                    {
                        EventType = "IMPORT_SUMMARY",
                        EntityType = "TransactionCsv",
                        EntityId = "transactions-csv",
                        NewValues = new
                        {
                            Status = status,
                            FileName = safeFileName,
                            FileSize = safeFileSize,
                            ProcessedRows = processedRows,
                            result.ImportedCount,
                            result.SkippedCount,
                            result.ErrorCount,
                            CreatedCategories = createdCategories,
                            DurationMs = (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds,
                            FailureReason = failureReason
                        }
                    });
                }
            }
            finally
            {
                importLock.Release();
            }
        }

        private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < headers.Count; index++)
            {
                var normalized = NormalizeHeader(headers[index]);
                if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
                {
                    map[normalized] = index;
                }
            }

            return map;
        }

        private static void ValidateHeaders(IReadOnlyDictionary<string, int> headerMap)
        {
            if (!headerMap.ContainsKey("date") ||
                !headerMap.ContainsKey("description") ||
                !headerMap.ContainsKey("amount") ||
                !headerMap.ContainsKey("type"))
            {
                throw new InvalidOperationException("CSV headers must include Date, Description, Amount, and Type.");
            }
        }

        private static bool TryMapRow(
            IReadOnlyList<string> values,
            IReadOnlyDictionary<string, int> headerMap,
            out TransactionImportRowDto row,
            out string error)
        {
            row = new TransactionImportRowDto();
            error = string.Empty;

            var dateValue = GetCell(values, headerMap, "date");
            var descriptionValue = GetCell(values, headerMap, "description");
            var amountValue = GetCell(values, headerMap, "amount");
            var typeValue = GetCell(values, headerMap, "type");
            var categoryValue = GetCell(values, headerMap, "category", "categoryname");

            if (string.IsNullOrWhiteSpace(dateValue))
            {
                error = "Date is required.";
                return false;
            }

            if (!TryParseDate(dateValue, out var date))
            {
                error = "Date format is invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(amountValue) ||
                !decimal.TryParse(amountValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                error = "Amount is invalid.";
                return false;
            }

            if (amount <= 0m || amount > 999_999_999m)
            {
                error = "Amount must be greater than 0 and within supported limits.";
                return false;
            }

            if (!Enum.TryParse<TransactionType>(typeValue, true, out var type))
            {
                error = "Type must be either Income or Expense.";
                return false;
            }

            var normalizedDescription = NormalizeDescription(descriptionValue);
            if (!string.IsNullOrWhiteSpace(descriptionValue) && normalizedDescription == null)
            {
                error = "Description is invalid.";
                return false;
            }

            if (normalizedDescription != null && normalizedDescription.Length > 250)
            {
                error = "Description exceeds the maximum length.";
                return false;
            }

            var normalizedCategoryName = NormalizeCategoryName(categoryValue);
            if (normalizedCategoryName != null && normalizedCategoryName.Length > 100)
            {
                // Invalid category text should safely fall back to "Uncategorized".
                normalizedCategoryName = null;
            }

            row = new TransactionImportRowDto
            {
                Date = date,
                Description = normalizedDescription,
                Amount = Math.Round(Math.Abs(amount), 2),
                CategoryName = normalizedCategoryName,
                Type = type
            };

            return true;
        }

        private static string GetCell(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> headerMap,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (headerMap.TryGetValue(key, out var index) && index >= 0 && index < row.Count)
                {
                    return row[index]?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            if (DateTime.TryParseExact(
                    value.Trim(),
                    SupportedDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedExact))
            {
                date = DateTime.SpecifyKind(parsedExact.Date, DateTimeKind.Utc);
                return true;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                date = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                return true;
            }

            date = default;
            return false;
        }

        private async Task EnsureUncategorizedCategoriesAsync(
            string userId,
            IDictionary<string, Category> categoryMap)
        {
            var missingTypes = new List<TransactionType>();
            foreach (var type in Enum.GetValues<TransactionType>())
            {
                var key = BuildCategoryKey("Uncategorized", type);
                if (!categoryMap.ContainsKey(key))
                {
                    missingTypes.Add(type);
                }
            }

            if (missingTypes.Count == 0)
            {
                return;
            }

            foreach (var type in missingTypes)
            {
                var category = new Category
                {
                    UserId = userId,
                    Name = "Uncategorized",
                    Type = type,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Categories.Add(category);
                categoryMap[BuildCategoryKey(category.Name, category.Type)] = category;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Remove failed tracked inserts so subsequent saves can continue safely.
                foreach (var entry in _context.ChangeTracker.Entries<Category>())
                {
                    if (entry.State == EntityState.Added &&
                        entry.Entity.UserId == userId &&
                        string.Equals(entry.Entity.Name, "Uncategorized", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                foreach (var type in missingTypes)
                {
                    var existing = await _context.Categories
                        .FirstOrDefaultAsync(c =>
                            c.UserId == userId &&
                            c.Type == type &&
                            c.Name.ToLower() == "uncategorized");

                    if (existing != null)
                    {
                        categoryMap[BuildCategoryKey(existing.Name, existing.Type)] = existing;
                    }
                }
            }
        }

        private async Task<(Category? Category, bool CreatedCategory)> ResolveCategoryAsync(
            string userId,
            TransactionImportRowDto row,
            IDictionary<string, Category> categoryMap)
        {
            if (!string.IsNullOrWhiteSpace(row.CategoryName))
            {
                var normalizedCategoryName = row.CategoryName.Trim();
                var categoryKey = BuildCategoryKey(normalizedCategoryName, row.Type);
                if (categoryMap.TryGetValue(categoryKey, out var matched))
                {
                    return (matched, false);
                }

                // Double-check persistent state to avoid duplicates when data differs only by casing.
                var normalizedCategoryNameLower = normalizedCategoryName.ToLowerInvariant();
                var existingCategory = await _context.Categories
                    .FirstOrDefaultAsync(c =>
                        c.UserId == userId &&
                        c.Type == row.Type &&
                        c.Name.ToLower() == normalizedCategoryNameLower);

                if (existingCategory != null)
                {
                    categoryMap[categoryKey] = existingCategory;
                    return (existingCategory, false);
                }

                // Auto-create missing category for this user/type so imported rows stay correctly classified.
                var newCategory = new Category
                {
                    UserId = userId,
                    Name = normalizedCategoryName,
                    Type = row.Type,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Categories.Add(newCategory);

                try
                {
                    await _context.SaveChangesAsync();
                    categoryMap[categoryKey] = newCategory;
                    return (newCategory, true);
                }
                catch (DbUpdateException)
                {
                    var entry = _context.Entry(newCategory);
                    if (entry.State != EntityState.Detached)
                    {
                        entry.State = EntityState.Detached;
                    }

                    // In race scenarios, resolve the category created by another request.
                    existingCategory = await _context.Categories
                        .FirstOrDefaultAsync(c =>
                            c.UserId == userId &&
                            c.Type == row.Type &&
                            c.Name.ToLower() == normalizedCategoryNameLower);

                    if (existingCategory != null)
                    {
                        categoryMap[categoryKey] = existingCategory;
                        return (existingCategory, false);
                    }
                }
            }

            var fallbackKey = BuildCategoryKey("Uncategorized", row.Type);
            if (categoryMap.TryGetValue(fallbackKey, out var fallback))
            {
                return (fallback, false);
            }

            await EnsureUncategorizedCategoriesAsync(userId, categoryMap);
            if (categoryMap.TryGetValue(fallbackKey, out fallback))
            {
                return (fallback, false);
            }

            return (null, false);
        }

        private async Task<bool> IsDuplicateTransactionAsync(
            string userId,
            DateTime transactionDateUtc,
            decimal amount,
            string? description)
        {
            var start = NormalizeStartUtc(transactionDateUtc);
            var end = NormalizeEndUtc(transactionDateUtc);
            var normalizedDescription = (description ?? string.Empty).Trim().ToLowerInvariant();

            return await _context.Transactions
                .AsNoTracking()
                .AnyAsync(t =>
                    t.UserId == userId &&
                    t.Amount == amount &&
                    t.TransactionDate >= start &&
                    t.TransactionDate <= end &&
                    (t.Description ?? string.Empty).ToLower() == normalizedDescription);
        }

        private void RegisterRowError(TransactionImportResultDto result, int lineNumber, string message)
        {
            result.SkippedCount++;
            result.ErrorCount++;

            if (result.Errors.Count < MaxErrorMessages)
            {
                result.Errors.Add($"Line {lineNumber}: {message}");
            }

            _logger.LogWarning("CSV import row skipped at line {LineNumber}: {Reason}", lineNumber, message);
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
                    "Audit logging failed in transaction import flow for {EventType}/{EntityType}/{EntityId}.",
                    request.EventType,
                    request.EntityType,
                    request.EntityId);
            }
        }

        private static IReadOnlyList<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var character = line[i];
                if (character == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (character == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(character);
            }

            values.Add(current.ToString());
            return values;
        }

        private static string NormalizeHeader(string header)
        {
            var normalized = new string((header ?? string.Empty)
                .Trim()
                .Where(character => !char.IsWhiteSpace(character) && character != '_' && character != '-')
                .ToArray())
                .ToLowerInvariant();

            return normalized switch
            {
                "transactiondate" => "date",
                "details" => "description",
                "desc" => "description",
                "note" => "description",
                "categoryname" => "category",
                _ => normalized
            };
        }

        private static string BuildCategoryKey(string name, TransactionType type)
        {
            return $"{name.Trim().ToLowerInvariant()}|{type}";
        }

        private static string BuildDuplicateKey(DateTime date, decimal amount, string? description)
        {
            var normalizedDescription = (description ?? string.Empty).Trim().ToLowerInvariant();
            return $"{date:yyyy-MM-dd}|{amount:0.00}|{normalizedDescription}";
        }

        private static string? NormalizeDescription(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var filtered = new string(value
                .Where(character => !char.IsControl(character))
                .ToArray())
                .Trim();

            return string.IsNullOrWhiteSpace(filtered)
                ? null
                : filtered;
        }

        private static string? NormalizeCategoryName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var filtered = new string(value
                .Where(character => !char.IsControl(character))
                .ToArray())
                .Trim();

            return string.IsNullOrWhiteSpace(filtered)
                ? null
                : filtered;
        }

        private static DateTime NormalizeStartUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }

        private static DateTime NormalizeEndUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        }
    }
}
