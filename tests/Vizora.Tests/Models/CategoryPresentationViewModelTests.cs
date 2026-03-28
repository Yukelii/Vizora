using Vizora.Models;

namespace Vizora.Tests.Models;

public class CategoryPresentationViewModelTests
{
    [Fact]
    public void FromCategory_WithCustomIconAndColor_UsesSavedVisuals()
    {
        var category = new Category
        {
            Name = "Travel",
            IconKey = "flight",
            ColorKey = "indigo",
            Type = TransactionType.Expense
        };

        var presentation = CategoryPresentationViewModel.FromCategory(category, "Fallback");

        Assert.Equal("Travel", presentation.Name);
        Assert.Equal("flight", presentation.IconKey);
        Assert.Equal("indigo", presentation.ColorKey);
    }

    [Fact]
    public void FromCategory_WithNullCategory_UsesFallbackAndDefaults()
    {
        var presentation = CategoryPresentationViewModel.FromCategory(null, "  Uncategorized  ");

        Assert.Equal("Uncategorized", presentation.Name);
        Assert.Equal(CategoryVisualCatalog.DefaultIconKey, presentation.IconKey);
        Assert.Equal(CategoryVisualCatalog.DefaultColorKey, presentation.ColorKey);
    }

    [Fact]
    public void FromCategory_WithInvalidVisualValues_FallsBackToDefaults()
    {
        var category = new Category
        {
            Name = "Utilities",
            IconKey = "invalid_icon",
            ColorKey = "invalid_color",
            Type = TransactionType.Expense
        };

        var presentation = CategoryPresentationViewModel.FromCategory(category);

        Assert.Equal("Utilities", presentation.Name);
        Assert.Equal(CategoryVisualCatalog.DefaultIconKey, presentation.IconKey);
        Assert.Equal(CategoryVisualCatalog.DefaultColorKey, presentation.ColorKey);
    }
}
