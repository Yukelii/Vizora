using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class BudgetPeriod
    {
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        [Required]
        public BudgetPeriodType Type { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
    }
}
