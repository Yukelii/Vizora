namespace Vizora.Models
{
    public sealed class ConcurrencyConflictResult<T>
    {
        public required T CurrentValues { get; init; }

        public required T DatabaseValues { get; init; }
    }
}
