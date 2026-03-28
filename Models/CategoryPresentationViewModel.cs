namespace Vizora.Models
{
    public sealed class CategoryPresentationViewModel
    {
        public string Name { get; init; } = "Uncategorized";

        public string IconKey { get; init; } = CategoryVisualCatalog.DefaultIconKey;

        public string ColorKey { get; init; } = CategoryVisualCatalog.DefaultColorKey;

        public string IconLabel => CategoryVisualCatalog.GetIconLabel(IconKey);

        public string ColorLabel => CategoryVisualCatalog.GetColorLabel(ColorKey);

        public static CategoryPresentationViewModel FromCategory(
            Category? category,
            string fallbackName = "Uncategorized")
        {
            var resolvedName = string.IsNullOrWhiteSpace(category?.Name)
                ? NormalizeName(fallbackName)
                : category!.Name.Trim();

            return new CategoryPresentationViewModel
            {
                Name = resolvedName,
                IconKey = CategoryVisualCatalog.ResolveIconKeyOrDefault(category?.IconKey),
                ColorKey = CategoryVisualCatalog.ResolveColorKeyOrDefault(category?.ColorKey)
            };
        }

        private static string NormalizeName(string? name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? "Uncategorized"
                : name.Trim();
        }
    }
}
