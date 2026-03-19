using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public TransactionType Type { get; set; }

        [Required]
        [StringLength(40)]
        public string IconKey { get; set; } = CategoryVisualCatalog.DefaultIconKey;

        [Required]
        [StringLength(20)]
        public string ColorKey { get; set; } = CategoryVisualCatalog.DefaultColorKey;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();

        public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = new List<RecurringTransaction>();
    }
}
