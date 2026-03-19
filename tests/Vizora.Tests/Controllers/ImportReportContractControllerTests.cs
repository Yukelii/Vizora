using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Vizora.Controllers;
using Vizora.DTOs;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Tests.Controllers;

public class ImportReportContractControllerTests
{
    [Fact]
    public async Task Import_WhenServiceReturnsPartialSuccess_StoresStandardizedFeedback()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new TransactionsController(
            new StubTransactionService(),
            new StubCategoryService(),
            new StubImportService(new TransactionImportResultDto
            {
                Status = OperationOutcomeStatus.PartialSuccess,
                UserMessage = "Import completed with warnings.",
                ImportedCount = 1,
                DuplicateCount = 1,
                RejectedCount = 1,
                ProcessedCount = 3,
                Issues =
                {
                    new OperationIssueDto
                    {
                        Code = "ROW_VALIDATION",
                        Message = "Line 3: Amount is invalid."
                    }
                }
            }),
            NullLogger<TransactionsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = CreateTempData(httpContext)
        };

        var result = await controller.Import(CreateCsvFile("Date,Description,Amount,Type\n2026-01-01,Lunch,10,Expense"));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Reports", redirect.ControllerName);
        Assert.True(controller.TempData.ContainsKey("OperationFeedback"));
    }

    [Fact]
    public async Task ExportTransactionsCsv_WhenServiceReturnsEmpty_RedirectsWithFeedback()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new ReportsController(
            new StubReportService(new TransactionReportExportResultDto
            {
                Status = OperationOutcomeStatus.Empty,
                UserMessage = "No transactions matched your export filters."
            }),
            NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = CreateTempData(httpContext)
        };

        var result = await controller.ExportTransactionsCsv(new TransactionReportExportRequestDto());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.True(controller.TempData.ContainsKey("OperationFeedback"));
    }

    [Fact]
    public async Task ExportTransactionsCsv_WhenServiceReturnsSuccess_ReturnsFile()
    {
        var controller = new ReportsController(
            new StubReportService(new TransactionReportExportResultDto
            {
                Status = OperationOutcomeStatus.Success,
                UserMessage = "Transaction export is ready.",
                Content = Encoding.UTF8.GetBytes("header\nvalue"),
                FileName = "transactions.csv",
                ContentType = "text/csv"
            }),
            NullLogger<ReportsController>.Instance);

        var result = await controller.ExportTransactionsCsv(new TransactionReportExportRequestDto());

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("transactions.csv", file.FileDownloadName);
        Assert.Equal("text/csv", file.ContentType);
    }

    private static TempDataDictionary CreateTempData(HttpContext httpContext)
    {
        return new TempDataDictionary(httpContext, new StubTempDataProvider());
    }

    private static IFormFile CreateCsvFile(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "csvFile", "transactions.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class StubTransactionService : ITransactionService
    {
        public Task<IReadOnlyList<Transaction>> GetAllAsync()
        {
            throw new NotSupportedException();
        }

        public Task<PagedResult<Transaction>> GetPagedAsync(TransactionListQuery query)
        {
            throw new NotSupportedException();
        }

        public Task<Transaction?> GetByIdAsync(int id)
        {
            throw new NotSupportedException();
        }

        public Task CreateAsync(Transaction transaction)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateAsync(Transaction transaction)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(int id)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubCategoryService : ICategoryService
    {
        public Task<IReadOnlyList<Category>> GetAllAsync(CategoryListFilter filter = CategoryListFilter.All)
        {
            return Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>());
        }

        public Task<Category?> GetByIdAsync(int id)
        {
            throw new NotSupportedException();
        }

        public Task CreateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateAsync(Category category)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(int id)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubImportService : ITransactionImportService
    {
        private readonly TransactionImportResultDto _result;

        public StubImportService(TransactionImportResultDto result)
        {
            _result = result;
        }

        public Task<TransactionImportResultDto> ImportCsvAsync(IFormFile file)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class StubReportService : ITransactionReportService
    {
        private readonly TransactionReportExportResultDto _result;

        public StubReportService(TransactionReportExportResultDto result)
        {
            _result = result;
        }

        public Task<TransactionReportExportResultDto> ExportTransactionsCsvAsync(TransactionReportExportRequestDto? request = null)
        {
            return Task.FromResult(_result);
        }
    }
}
