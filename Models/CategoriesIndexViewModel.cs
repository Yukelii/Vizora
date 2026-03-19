namespace Vizora.Models
{
    public class CategoriesIndexViewModel
    {
        public IReadOnlyList<Category> Categories { get; set; } = Array.Empty<Category>();

        public CategoryListFilter Filter { get; set; } = CategoryListFilter.All;

        public int TotalCategories { get; set; }

        public int ExpenseCategories { get; set; }

        public int IncomeCategories { get; set; }
    }
}
