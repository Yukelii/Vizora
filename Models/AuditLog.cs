using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string EntityType { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string EntityId { get; set; } = string.Empty;

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        [StringLength(64)]
        public string IpAddress { get; set; } = string.Empty;

        public string OldValues { get; set; } = string.Empty;

        public string NewValues { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
