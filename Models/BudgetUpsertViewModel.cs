using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class BudgetUpsertViewModel
    {
        public int? Id { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        [Required]
        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999999")]
        [Display(Name = "Planned Amount")]
        public decimal PlannedAmount { get; set; }

        [Required]
        [Display(Name = "Budget Period")]
        public BudgetPeriodType PeriodType { get; set; } = BudgetPeriodType.Monthly;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; } = DateTime.UtcNow.Date;
    }
}
