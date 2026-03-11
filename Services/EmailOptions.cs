namespace Vizora.Services
{
    public class EmailOptions
    {
        public const string SectionName = "Email";

        public string? FromAddress { get; set; }

        public string? FromName { get; set; }

        public string? Host { get; set; }

        public int Port { get; set; } = 587;

        public bool EnableSsl { get; set; } = true;

        public string? UserName { get; set; }

        public string? Password { get; set; }
    }
}
