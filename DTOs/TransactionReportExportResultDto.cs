namespace Vizora.DTOs
{
    public class TransactionReportExportResultDto : OperationResultDto
    {
        public byte[]? Content { get; set; }

        public int RowCount { get; set; }

        public string ContentType { get; set; } = "text/csv";

        public string FileName { get; set; } = "vizora-transactions.csv";
    }
}
