namespace Vizora.Models
{
    public static class CategoryVisualCatalog
    {
        public const string DefaultIconKey = "receipt_long";
        public const string DefaultColorKey = "slate";

        private static readonly string[] SuggestedIcons =
        {
            "shopping_cart",
            "restaurant",
            "home",
            "directions_car",
            "fitness_center",
            "payments"
        };

        private static readonly string[] MoreIcons =
        {
            "flight",
            "favorite",
            "work",
            "school",
            "local_hospital",
            "sports_esports",
            "movie",
            "pets",
            "savings",
            "receipt_long"
        };

        private static readonly string[] ColorKeysInternal =
        {
            "teal",
            "blue",
            "amber",
            "purple",
            "rose",
            "emerald",
            "orange",
            "indigo",
            "slate",
            "red"
        };

        private static readonly HashSet<string> IconKeySet = new(
            SuggestedIcons.Concat(MoreIcons),
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ColorKeySet = new(
            ColorKeysInternal,
            StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> SuggestedIconKeys => SuggestedIcons;

        public static IReadOnlyList<string> MoreIconKeys => MoreIcons;

        public static IReadOnlyList<string> AllIconKeys => SuggestedIcons.Concat(MoreIcons).ToArray();

        public static IReadOnlyList<string> ColorKeys => ColorKeysInternal;

        public static bool IsValidIconKey(string? iconKey)
        {
            if (string.IsNullOrWhiteSpace(iconKey))
            {
                return false;
            }

            return IconKeySet.Contains(iconKey.Trim());
        }

        public static bool IsValidColorKey(string? colorKey)
        {
            if (string.IsNullOrWhiteSpace(colorKey))
            {
                return false;
            }

            return ColorKeySet.Contains(colorKey.Trim());
        }

        public static string ResolveIconKeyOrDefault(string? iconKey)
        {
            var normalized = Normalize(iconKey, DefaultIconKey);
            return IsValidIconKey(normalized)
                ? normalized
                : DefaultIconKey;
        }

        public static string ResolveColorKeyOrDefault(string? colorKey)
        {
            var normalized = Normalize(colorKey, DefaultColorKey);
            return IsValidColorKey(normalized)
                ? normalized
                : DefaultColorKey;
        }

        public static string GetIconLabel(string iconKey)
        {
            return ResolveIconKeyOrDefault(iconKey) switch
            {
                "shopping_cart" => "Shopping cart",
                "restaurant" => "Restaurant",
                "home" => "Home",
                "flight" => "Flight",
                "directions_car" => "Transportation",
                "fitness_center" => "Fitness",
                "favorite" => "Health",
                "payments" => "Payments",
                "work" => "Work",
                "school" => "School",
                "local_hospital" => "Hospital",
                "sports_esports" => "Gaming",
                "movie" => "Movies",
                "pets" => "Pets",
                "savings" => "Savings",
                _ => "Receipt"
            };
        }

        public static string GetColorLabel(string colorKey)
        {
            return ResolveColorKeyOrDefault(colorKey) switch
            {
                "teal" => "Teal",
                "blue" => "Blue",
                "amber" => "Amber",
                "purple" => "Purple",
                "rose" => "Rose",
                "emerald" => "Emerald",
                "orange" => "Orange",
                "indigo" => "Indigo",
                "red" => "Red",
                _ => "Slate"
            };
        }

        private static string Normalize(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim().ToLowerInvariant();
        }
    }
}
