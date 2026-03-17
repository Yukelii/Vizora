using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class CategoryUpsertViewModel
    {
        public int Id { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public TransactionType Type { get; set; }
    }
}
