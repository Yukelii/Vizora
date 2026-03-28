namespace Vizora.Models
{
    public sealed class ModalConflictBannerViewModel
    {
        public string Message { get; set; } =
            "This record was updated by another session. Reload latest data or review your changes and retry.";

        public string ReloadUrl { get; set; } = string.Empty;

        public string ReloadModalTitle { get; set; } = "Reload Latest";

        public IList<ConcurrencyFieldComparisonViewModel> FieldComparisons { get; set; } =
            new List<ConcurrencyFieldComparisonViewModel>();

        public bool AllowOverwrite { get; set; }

        public string OverwriteFieldName { get; set; } = "ForceOverwrite";

        public string OverwriteConfirmationLabel { get; set; } =
            "I understand this will overwrite the latest saved values with my changes.";

        public string OverwriteButtonLabel { get; set; } = "Overwrite Latest";
    }
}
