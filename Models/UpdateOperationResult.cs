namespace Vizora.Models
{
    public enum UpdateOperationStatus
    {
        Success,
        NotFound,
        ValidationFailed,
        Conflict
    }

    public sealed class UpdateOperationResult<TConflict>
    {
        public UpdateOperationStatus Status { get; init; }

        public string? ErrorMessage { get; init; }

        public ConcurrencyConflictResult<TConflict>? Conflict { get; init; }

        public static UpdateOperationResult<TConflict> Success() => new()
        {
            Status = UpdateOperationStatus.Success
        };

        public static UpdateOperationResult<TConflict> NotFound() => new()
        {
            Status = UpdateOperationStatus.NotFound
        };

        public static UpdateOperationResult<TConflict> ValidationFailed(string message) => new()
        {
            Status = UpdateOperationStatus.ValidationFailed,
            ErrorMessage = message
        };

        public static UpdateOperationResult<TConflict> ConflictDetected(
            ConcurrencyConflictResult<TConflict> conflict,
            string message) => new()
        {
            Status = UpdateOperationStatus.Conflict,
            ErrorMessage = message,
            Conflict = conflict
        };
    }
}
