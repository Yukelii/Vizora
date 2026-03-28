using Vizora.Models;

namespace Vizora.Tests.Models;

public class TransactionListItemViewModelTests
{
    [Fact]
    public void FromTransaction_WithLinkedCategory_UsesCategoryVisuals()
    {
        var transaction = new Transaction
        {
            Id = 5,
            CategoryId = 9,
            Type = TransactionType.Expense,
            Amount = 420m,
            Description = "Gym membership",
            TransactionDate = new DateTime(2026, 3, 1),
            Category = new Category
            {
                Id = 9,
                Name = "Fitness",
                Type = TransactionType.Expense,
                IconKey = "fitness_center",
                ColorKey = "emerald"
            }
        };

        var row = TransactionListItemViewModel.FromTransaction(transaction);

        Assert.Equal(9, row.CategoryId);
        Assert.Equal("Fitness", row.CategoryPresentation.Name);
        Assert.Equal("fitness_center", row.CategoryPresentation.IconKey);
        Assert.Equal("emerald", row.CategoryPresentation.ColorKey);
    }

    [Fact]
    public void FromTransaction_WithoutLinkedCategory_UsesFallbackPresentation()
    {
        var transaction = new Transaction
        {
            Id = 5,
            CategoryId = 999,
            Type = TransactionType.Expense,
            Amount = 25m,
            Description = "Legacy orphaned row",
            TransactionDate = new DateTime(2026, 3, 1),
            Category = null
        };

        var row = TransactionListItemViewModel.FromTransaction(transaction);

        Assert.Equal("Uncategorized", row.CategoryPresentation.Name);
        Assert.Equal(CategoryVisualCatalog.DefaultIconKey, row.CategoryPresentation.IconKey);
        Assert.Equal(CategoryVisualCatalog.DefaultColorKey, row.CategoryPresentation.ColorKey);
    }
}
