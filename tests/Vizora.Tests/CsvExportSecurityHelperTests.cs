using Vizora.Services;

namespace Vizora.Tests;

public class CsvExportSecurityHelperTests
{
    [Fact]
    public void SanitizeForCsv_LeavesNormalInputUnchanged()
    {
        var result = CsvExportSecurityHelper.SanitizeForCsv("Groceries");

        Assert.Equal("Groceries", result);
    }

    [Fact]
    public void SanitizeForCsv_PrefixesFormulaInjectionInput()
    {
        var result = CsvExportSecurityHelper.SanitizeForCsv("=SUM(A1:A2)");

        Assert.Equal("'=SUM(A1:A2)", result);
    }

    [Fact]
    public void SanitizeForCsv_PrefixesHyperlinkInjectionInput()
    {
        var result = CsvExportSecurityHelper.SanitizeForCsv("=HYPERLINK(\"http://evil.com\")");

        Assert.Equal("'=HYPERLINK(\"http://evil.com\")", result);
    }

    [Fact]
    public void SanitizeAndEscape_QuotesAndEscapesSanitizedContent()
    {
        var result = CsvExportSecurityHelper.SanitizeAndEscape("=HYPERLINK(\"http://evil.com\")");

        Assert.Equal("\"'=HYPERLINK(\"\"http://evil.com\"\")\"", result);
    }
}
