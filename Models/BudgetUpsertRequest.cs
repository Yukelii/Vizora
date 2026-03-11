namespace Vizora.Models
{
    public class BudgetUpsertRequest
    {
        public int CategoryId { get; set; }

        public decimal PlannedAmount { get; set; }

        public BudgetPeriodType PeriodType { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }
}
