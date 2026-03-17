namespace Vizora.DTOs
{
    public enum TransactionImportStatus
    {
        Success = 1,
        PartialSuccess = 2,
        Failed = 3
    }

    public class TransactionImportResultDto
    {
        public int ProcessedCount { get; set; }

        public int ImportedCount { get; set; }

        public int DuplicateCount { get; set; }

        public int RejectedCount { get; set; }

        public int SkippedCount { get; set; }

        public int ErrorCount { get; set; }

        public TransactionImportStatus Status { get; set; } = TransactionImportStatus.Success;

        public string? FailureMessage { get; set; }

        public List<string> Errors { get; set; } = new();
    }
}
