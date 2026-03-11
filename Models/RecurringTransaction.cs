using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vizora.Enums;

namespace Vizora.Models
{
    public class RecurringTransaction
    {
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        [Required]
        public int CategoryId { get; set; }

        public Category? Category { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }

        [Required]
        public TransactionType Type { get; set; }

        [StringLength(250)]
        public string? Description { get; set; }

        public RecurringFrequency Frequency { get; set; }

        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        public DateTime NextRunDate { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
