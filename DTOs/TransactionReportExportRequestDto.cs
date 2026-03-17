using Vizora.Models;

namespace Vizora.DTOs
{
    public class TransactionReportExportRequestDto
    {
        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public int? CategoryId { get; set; }

        public TransactionType? Type { get; set; }
    }
}
