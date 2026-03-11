namespace Vizora.DTOs
{
    public enum FinancialInsightSeverity
    {
        Info = 1,
        Warning = 2,
        Alert = 3
    }

    public class FinancialInsightDto
    {
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public FinancialInsightSeverity Severity { get; set; } = FinancialInsightSeverity.Info;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
