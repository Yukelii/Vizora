namespace Vizora.Models
{
    public sealed class ModalSubmitResult
    {
        public string Status { get; set; } = "success";

        public bool ReloadPage { get; set; } = true;

        public string? RedirectUrl { get; set; }

        public string? Message { get; set; }
    }
}
