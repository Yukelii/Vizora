namespace Vizora.Models
{
    public sealed class CategoryConflictSnapshot
    {
        public string RowVersionHex { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public TransactionType Type { get; set; }

        public string IconKey { get; set; } = CategoryVisualCatalog.DefaultIconKey;

        public string ColorKey { get; set; } = CategoryVisualCatalog.DefaultColorKey;
    }
}
