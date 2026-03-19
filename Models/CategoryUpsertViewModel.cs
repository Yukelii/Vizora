using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class CategoryUpsertViewModel : IValidatableObject
    {
        public int Id { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public bool ForceOverwrite { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public TransactionType Type { get; set; } = TransactionType.Expense;

        [Required]
        [Display(Name = "Icon")]
        [StringLength(40)]
        public string IconKey { get; set; } = CategoryVisualCatalog.DefaultIconKey;

        [Required]
        [Display(Name = "Color")]
        [StringLength(20)]
        public string ColorKey { get; set; } = CategoryVisualCatalog.DefaultColorKey;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var normalizedIconKey = Normalize(IconKey);
            var normalizedColorKey = Normalize(ColorKey);

            if (!CategoryVisualCatalog.IsValidIconKey(normalizedIconKey))
            {
                yield return new ValidationResult(
                    "Selected icon is not supported.",
                    new[] { nameof(IconKey) });
            }
            else
            {
                IconKey = normalizedIconKey;
            }

            if (!CategoryVisualCatalog.IsValidColorKey(normalizedColorKey))
            {
                yield return new ValidationResult(
                    "Selected color is not supported.",
                    new[] { nameof(ColorKey) });
            }
            else
            {
                ColorKey = normalizedColorKey;
            }
        }

        private static string Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }
}
