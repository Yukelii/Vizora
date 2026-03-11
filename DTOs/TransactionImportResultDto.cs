namespace Vizora.DTOs
{
    public class TransactionImportResultDto
    {
        public int ImportedCount { get; set; }

        public int SkippedCount { get; set; }

        public int ErrorCount { get; set; }

        public List<string> Errors { get; set; } = new();
    }
}
