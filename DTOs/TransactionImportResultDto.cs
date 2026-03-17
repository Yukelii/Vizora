namespace Vizora.DTOs
{
    public class TransactionImportResultDto : OperationResultDto
    {
        public int ProcessedCount { get; set; }

        public int ImportedCount { get; set; }

        public int DuplicateCount { get; set; }

        public int RejectedCount { get; set; }

        public int SkippedCount { get; set; }

        public int ErrorCount { get; set; }
    }
}
