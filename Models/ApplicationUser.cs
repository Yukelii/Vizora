using Microsoft.AspNetCore.Identity;

namespace Vizora.Models
{
    public class ApplicationUser : IdentityUser
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Category> Categories { get; set; } = new List<Category>();

        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        public ICollection<BudgetPeriod> BudgetPeriods { get; set; } = new List<BudgetPeriod>();

        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();

        public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = new List<RecurringTransaction>();
    }
}
