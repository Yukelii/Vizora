namespace Vizora.Services
{
    public static class CsvExportSecurityHelper
    {
        public static string SanitizeForCsv(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.AsSpan().TrimStart();
            if (trimmed.Length > 0 && IsFormulaPrefix(trimmed[0]))
            {
                return "'" + value;
            }

            return value;
        }

        public static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        public static string SanitizeAndEscape(string? value)
        {
            return EscapeCsv(SanitizeForCsv(value));
        }

        private static bool IsFormulaPrefix(char character)
        {
            return character is '=' or '+' or '-' or '@';
        }
    }
}
