using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Data;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;
using Vizora.Tests.TestInfrastructure;

namespace Vizora.Tests.Services;

public class TransactionImportServiceTests
{
    private const string OtherUserId = "test-user-2";

    [Fact]
    public async Task ImportCsvAsync_WithValidRows_ImportsTransactionsForCurrentUser()
    {
        await using var context = TestDbContextFactory.Create();
        var expenseCategory = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var incomeCategory = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Salary", TransactionType.Income);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-01-01,Lunch,25.50,Expense,Food
            2026-01-02,Payroll,1000.00,Income,Salary
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));
        var rows = await context.Transactions
            .AsNoTracking()
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(OperationOutcomeStatus.Success, result.Status);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(2, result.ProcessedCount);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(TestDataSeeder.DefaultUserId, row.UserId));
        Assert.Contains(rows, row => row.CategoryId == expenseCategory.Id && row.Type == TransactionType.Expense && row.Amount == 25.50m);
        Assert.Contains(rows, row => row.CategoryId == incomeCategory.Id && row.Type == TransactionType.Income && row.Amount == 1000m);
    }

    [Fact]
    public async Task ImportCsvAsync_WithInvalidRows_ReturnsErrorsAndSkipsInvalidRows()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-01-01,Valid Row,10.25,Expense,Food
            2026-01-02,Invalid Amount,abc,Expense,Food
            ,Missing Date,9.00,Expense,Food
            2026-01-03,Invalid Type,8.00,Transfer,Food
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(OperationOutcomeStatus.PartialSuccess, result.Status);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(3, result.RejectedCount);
        Assert.Equal(3, result.ErrorCount);
        Assert.Equal(4, result.ProcessedCount);
        Assert.Equal(1, await context.Transactions.CountAsync());
        Assert.Equal(3, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal("ROW_VALIDATION", issue.Code));
    }

    [Fact]
    public async Task ImportCsvAsync_SkipsFileAndDatabaseDuplicates()
    {
        await using var context = TestDbContextFactory.Create();
        var category = TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        context.Transactions.Add(new Transaction
        {
            UserId = TestDataSeeder.DefaultUserId,
            CategoryId = category.Id,
            Type = TransactionType.Expense,
            Amount = 10m,
            Description = "Coffee",
            TransactionDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-01-05,Coffee,10.00,Expense,Food
            2026-01-05,Coffee,10.00,Expense,Food
            2026-01-06,Snack,5.00,Expense,Food
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));
        var rows = await context.Transactions.AsNoTracking().OrderBy(t => t.TransactionDate).ToListAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(OperationOutcomeStatus.PartialSuccess, result.Status);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(2, result.DuplicateCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(3, result.ProcessedCount);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Description == "Snack");
    }

    [Fact]
    public async Task ImportCsvAsync_DoesNotCollapseRows_WhenTypeOrCategoryDiffers()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Salary", TransactionType.Income);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-04-01,Repeat-like,100.00,Expense,Food
            2026-04-01,Repeat-like,100.00,Income,Salary
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));
        var rows = await context.Transactions.AsNoTracking().OrderBy(t => t.Type).ToListAsync();

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(OperationOutcomeStatus.Success, result.Status);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Type == TransactionType.Expense);
        Assert.Contains(rows, row => row.Type == TransactionType.Income);
    }

    [Fact]
    public async Task ImportCsvAsync_WhenAllRowsRejected_ReturnsFailedStatus()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            ,Missing Date,10.00,Expense,Food
            2026-03-01,Bad Type,10.00,Transfer,Food
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(OperationOutcomeStatus.ValidationFailure, result.Status);
        Assert.Equal(2, result.ProcessedCount);
    }

    [Fact]
    public async Task ImportCsvAsync_CreatesCategoryForCurrentUser_WhenSameNameExistsForAnotherUser()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, OtherUserId, "Travel", TransactionType.Expense);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-02-01,Taxi,35.00,Expense,Travel
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));
        var userCategory = await context.Categories
            .AsNoTracking()
            .SingleAsync(c => c.UserId == TestDataSeeder.DefaultUserId && c.Name == "Travel" && c.Type == TransactionType.Expense);
        var imported = await context.Transactions.AsNoTracking().SingleAsync(t => t.UserId == TestDataSeeder.DefaultUserId);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(userCategory.Id, imported.CategoryId);
        Assert.Equal(TestDataSeeder.DefaultUserId, userCategory.UserId);
    }

    [Fact]
    public async Task ImportCsvAsync_UsesUncategorizedWhenCategoryTextIsInvalid()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);
        var tooLongCategory = new string('x', 120);

        var csv =
            $"""
            Date,Description,Amount,Type,Category
            2026-02-10,Long Category Name,18.00,Expense,{tooLongCategory}
            """;

        var result = await service.ImportCsvAsync(CreateCsvFile(csv));

        var uncategorized = await context.Categories
            .AsNoTracking()
            .SingleAsync(c =>
                c.UserId == TestDataSeeder.DefaultUserId &&
                c.Name == "Uncategorized" &&
                c.Type == TransactionType.Expense);
        var imported = await context.Transactions.AsNoTracking().SingleAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(uncategorized.Id, imported.CategoryId);
    }

    [Fact]
    public async Task ImportCsvAsync_WhenActiveDatabaseImportLockExists_ReturnsInvalidRequest()
    {
        await using var context = await CreateSqliteContextAsync();
        var user = TestDataSeeder.EnsureUser(context, TestDataSeeder.DefaultUserId);
        TestDataSeeder.EnsureCategory(context, user.Id, "Food", TransactionType.Expense);
        context.ImportExecutionLocks.Add(new ImportExecutionLock
        {
            UserId = user.Id,
            AcquiredAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var service = CreateService(context);

        var result = await service.ImportCsvAsync(CreateCsvFile(
            """
            Date,Description,Amount,Type,Category
            2026-01-01,Lunch,10,Expense,Food
            """));

        Assert.Equal(OperationOutcomeStatus.InvalidRequest, result.Status);
        Assert.Contains("already running", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Issues);
        Assert.Equal("IMPORT_CONCURRENT", result.Issues[0].Code);
    }

    [Fact]
    public async Task ImportCsvAsync_WhenDatabaseLockIsStale_RemovesLockAndProceeds()
    {
        await using var context = await CreateSqliteContextAsync();
        var user = TestDataSeeder.EnsureUser(context, TestDataSeeder.DefaultUserId);
        TestDataSeeder.EnsureCategory(context, user.Id, "Food", TransactionType.Expense);
        context.ImportExecutionLocks.Add(new ImportExecutionLock
        {
            UserId = user.Id,
            AcquiredAt = DateTime.UtcNow.AddHours(-2)
        });
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE ImportExecutionLocks SET AcquiredAt = datetime('now', '-2 hours') WHERE UserId = {0}",
            user.Id);
        context.ChangeTracker.Clear();

        var service = CreateService(context);
        var result = await service.ImportCsvAsync(CreateCsvFile(
            """
            Date,Description,Amount,Type,Category
            2026-01-01,Lunch,10,Expense,Food
            """));

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, await context.ImportExecutionLocks.CountAsync());
    }

    [Fact]
    public async Task ImportCsvAsync_WhenFileExtensionInvalid_ReturnsInvalidRequestResult()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);

        var result = await service.ImportCsvAsync(CreateCsvFile(
            "Date,Description,Amount,Type\n2026-01-01,Lunch,10,Expense",
            "transactions.txt"));

        Assert.Equal(OperationOutcomeStatus.InvalidRequest, result.Status);
        Assert.Contains("Only CSV files are supported", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Issues);
        Assert.Equal("IMPORT_INVALID_EXTENSION", result.Issues[0].Code);
    }

    [Fact]
    public async Task ImportCsvAsync_PreventsConcurrentImportForSameUser()
    {
        await using var context = TestDbContextFactory.Create();
        TestDataSeeder.EnsureCategory(context, TestDataSeeder.DefaultUserId, "Food", TransactionType.Expense);
        var service = CreateService(context);

        var csv =
            """
            Date,Description,Amount,Type,Category
            2026-03-01,Blocking Row,22.00,Expense,Food
            """;

        using var streamOpenStarted = new ManualResetEventSlim(false);
        using var releaseStreamOpen = new ManualResetEventSlim(false);
        var blockingFile = new BlockingFormFile(csv, streamOpenStarted, releaseStreamOpen);

        var firstImportTask = Task.Run(() => service.ImportCsvAsync(blockingFile));
        Assert.True(streamOpenStarted.Wait(TimeSpan.FromSeconds(5)), "Expected first import to hold the per-user import lock.");

        var concurrentResult = await service.ImportCsvAsync(CreateCsvFile(csv));
        Assert.Equal(OperationOutcomeStatus.InvalidRequest, concurrentResult.Status);
        Assert.Contains("already running", concurrentResult.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(concurrentResult.Issues);
        Assert.Equal("IMPORT_CONCURRENT", concurrentResult.Issues[0].Code);

        releaseStreamOpen.Set();
        var firstResult = await firstImportTask;
        Assert.Equal(1, firstResult.ImportedCount);
    }

    [Fact]
    public async Task ImportCsvAsync_WhenUnexpectedFailureOccurs_ReturnsSafeFailureContract()
    {
        await using var context = TestDbContextFactory.Create();
        var service = CreateService(context);

        var result = await service.ImportCsvAsync(new ThrowingFormFile());

        Assert.Equal(OperationOutcomeStatus.Failed, result.Status);
        Assert.Equal("Import failed due to an unexpected error. Please try again.", result.UserMessage);
        Assert.DoesNotContain("Simulated", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsDataTrusted);
        Assert.Single(result.Issues);
        Assert.Equal("IMPORT_UNEXPECTED", result.Issues[0].Code);
    }

    private static TransactionImportService CreateService(ApplicationDbContext context, string userId = TestDataSeeder.DefaultUserId)
    {
        return new TransactionImportService(
            context,
            new TestUserContextService(userId),
            new NoOpAuditService(),
            NullLogger<TransactionImportService>.Instance);
    }

    private static IFormFile CreateCsvFile(string content, string fileName = "transactions.csv")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "csvFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    private static async Task<ApplicationDbContext> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class BlockingFormFile : IFormFile
    {
        private readonly byte[] _content;
        private readonly ManualResetEventSlim _streamOpenStarted;
        private readonly ManualResetEventSlim _releaseStreamOpen;

        public BlockingFormFile(string content, ManualResetEventSlim streamOpenStarted, ManualResetEventSlim releaseStreamOpen)
        {
            _content = Encoding.UTF8.GetBytes(content);
            _streamOpenStarted = streamOpenStarted;
            _releaseStreamOpen = releaseStreamOpen;
        }

        public string ContentType { get; set; } = "text/csv";

        public string ContentDisposition { get; set; } = string.Empty;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public long Length => _content.Length;

        public string Name => "csvFile";

        public string FileName => "transactions.csv";

        public void CopyTo(Stream target)
        {
            using var stream = OpenReadStream();
            stream.CopyTo(target);
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            await using var stream = OpenReadStream();
            await stream.CopyToAsync(target, cancellationToken);
        }

        public Stream OpenReadStream()
        {
            _streamOpenStarted.Set();
            _releaseStreamOpen.Wait(TimeSpan.FromSeconds(10));
            return new MemoryStream(_content, writable: false);
        }
    }

    private sealed class ThrowingFormFile : IFormFile
    {
        public string ContentType { get; set; } = "text/csv";

        public string ContentDisposition { get; set; } = string.Empty;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public long Length => 10;

        public string Name => "csvFile";

        public string FileName => "transactions.csv";

        public void CopyTo(Stream target)
        {
            throw new InvalidOperationException("Simulated stream failure.");
        }

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated stream failure.");
        }

        public Stream OpenReadStream()
        {
            throw new InvalidOperationException("Simulated stream failure.");
        }
    }
}
