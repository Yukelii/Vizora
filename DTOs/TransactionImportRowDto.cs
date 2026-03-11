using Vizora.Models;

namespace Vizora.DTOs
{
    public class TransactionImportRowDto
    {
        public DateTime Date { get; set; }

        public string? Description { get; set; }

        public decimal Amount { get; set; }

        public string? CategoryName { get; set; }

        public TransactionType Type { get; set; }
    }
}
