namespace Vizora.Models
{
    public static class ModalUiState
    {
        public const string Idle = "idle";
        public const string Submitting = "submitting";
        public const string ValidationError = "validation_error";
        public const string Conflict = "conflict";
        public const string Success = "success";
        public const string Error = "error";

        public static string Normalize(string? state)
        {
            return (state ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                Submitting => Submitting,
                ValidationError => ValidationError,
                Conflict => Conflict,
                Success => Success,
                Error => Error,
                _ => Idle
            };
        }
    }
}
