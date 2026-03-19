namespace Vizora.Models
{
    public class CategoryListQuery
    {
        public CategoryListFilter Filter { get; set; } = CategoryListFilter.All;
    }

    public enum CategoryListFilter
    {
        All = 0,
        Expense = 1,
        Income = 2
    }
}
