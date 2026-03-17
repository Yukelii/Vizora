using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class ImportExecutionLock
    {
        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
    }
}
