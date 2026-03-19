namespace Vizora.Models
{
    public sealed class ConcurrencyFieldComparisonViewModel
    {
        public string FieldLabel { get; set; } = string.Empty;

        public string YourValue { get; set; } = string.Empty;

        public string LatestValue { get; set; } = string.Empty;
    }

    public sealed class ConcurrencyModalViewModel
    {
        public string EntityName { get; set; } = string.Empty;

        public string ReloadUrl { get; set; } = string.Empty;

        public string ReloadModalTitle { get; set; } = string.Empty;

        public string OverwriteActionUrl { get; set; } = string.Empty;

        public string OverwriteButtonLabel { get; set; } = "Overwrite";

        public IList<ConcurrencyFieldComparisonViewModel> FieldComparisons { get; set; } =
            new List<ConcurrencyFieldComparisonViewModel>();

        public IDictionary<string, string> HiddenFields { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
