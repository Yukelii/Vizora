using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class TransactionUpsertViewModel
    {
        public int? Id { get; set; }

        public string? RowVersion { get; set; }

        public bool ForceOverwrite { get; set; }

        [Required]
        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }

        [StringLength(250)]
        public string? Description { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Transaction Date")]
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow.Date;
    }
}
