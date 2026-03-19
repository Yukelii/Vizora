using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using System.Net.Sockets;

namespace Vizora.Services
{
    public interface IAccountEmailService
    {
        Task<bool> SendEmailAsync(
            string toEmail,
            string subject,
            string body,
            bool isHtml = true,
            CancellationToken cancellationToken = default);
    }

    public class AccountEmailService : IAccountEmailService
    {
        private readonly IOptionsSnapshot<EmailOptions> _emailOptions;
        private readonly IAccountSmtpClientFactory _smtpClientFactory;
        private readonly ILogger<AccountEmailService> _logger;

        private enum EmailSendStage
        {
            Connect,
            Authenticate,
            Send,
            Disconnect
        }

        public AccountEmailService(
            IOptionsSnapshot<EmailOptions> emailOptions,
            IAccountSmtpClientFactory smtpClientFactory,
            ILogger<AccountEmailService> logger)
        {
            _emailOptions = emailOptions;
            _smtpClientFactory = smtpClientFactory;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(
            string toEmail,
            string subject,
            string body,
            bool isHtml = true,
            CancellationToken cancellationToken = default)
        {
            // Validate SMTP options up front and fail safely when configuration is incomplete.
            if (!TryGetValidatedOptions(out var options, out var fromAddress))
            {
                return false;
            }

            if (!MailboxAddress.TryParse(toEmail, out var toAddress))
            {
                _logger.LogWarning("Email sending skipped due to invalid recipient email format.");
                return false;
            }

            // Compose a minimal RFC-compliant message with either HTML or plain text body.
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrWhiteSpace(options.FromName) ? fromAddress.Name : options.FromName,
                fromAddress.Address));
            message.To.Add(toAddress);
            message.Subject = subject;
            message.Body = new TextPart(isHtml ? TextFormat.Html : TextFormat.Plain) { Text = body };

            var secureSocketOptions = ResolveSecureSocketOptions(options.Port, options.EnableSsl);
            var stage = EmailSendStage.Connect;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = _smtpClientFactory.Create();

                // Connect and authenticate explicitly because most providers require SMTP auth.
                await client.ConnectAsync(
                    options.Host!,
                    options.Port,
                    secureSocketOptions,
                    cancellationToken);

                stage = EmailSendStage.Authenticate;
                await client.AuthenticateAsync(
                    options.UserName!,
                    options.Password!,
                    cancellationToken);

                stage = EmailSendStage.Send;
                await client.SendAsync(message, cancellationToken);

                stage = EmailSendStage.Disconnect;
                await client.DisconnectAsync(true, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                LogAuthenticationFailure(options, ex);
                return false;
            }
            catch (SmtpCommandException ex)
            {
                _logger.LogWarning(
                    ex,
                    "SMTP command failed during {Stage} stage for host {Host}:{Port} with status code {StatusCode}.",
                    ToStageText(stage),
                    options.Host,
                    options.Port,
                    ex.StatusCode);
                return false;
            }
            catch (SmtpProtocolException ex)
            {
                _logger.LogWarning(
                    ex,
                    "SMTP protocol failure during {Stage} stage for host {Host}:{Port}.",
                    ToStageText(stage),
                    options.Host,
                    options.Port);
                return false;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(
                    ex,
                    "SMTP socket failure during {Stage} stage for host {Host}:{Port}.",
                    ToStageText(stage),
                    options.Host,
                    options.Port);
                return false;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is FormatException ||
                ex is System.Security.Authentication.AuthenticationException)
            {
                _logger.LogWarning(
                    ex,
                    "SMTP email delivery failed during {Stage} stage for host {Host}:{Port}.",
                    ToStageText(stage),
                    options.Host,
                    options.Port);
                return false;
            }
        }

        private void LogAuthenticationFailure(EmailOptions options, MailKit.Security.AuthenticationException ex)
        {
            if (IsGmailHost(options.Host))
            {
                _logger.LogWarning(
                    ex,
                    "SMTP authentication failed during authenticate stage for host {Host}:{Port}. Gmail requires an App Password (not a regular account password) and TLS.",
                    options.Host,
                    options.Port);
                return;
            }

            _logger.LogWarning(
                ex,
                "SMTP authentication failed during authenticate stage for host {Host}:{Port}.",
                options.Host,
                options.Port);
        }

        private bool TryGetValidatedOptions(out EmailOptions options, out MailboxAddress fromAddress)
        {
            options = BuildEffectiveOptions(_emailOptions.Value);
            fromAddress = default!;

            // Common SMTP setup uses sender address as username.
            if (string.IsNullOrWhiteSpace(options.UserName) && !string.IsNullOrWhiteSpace(options.FromAddress))
            {
                options.UserName = options.FromAddress;
            }

            // Report all missing required fields in a single warning for easier diagnostics.
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Host))
            {
                missingFields.Add(nameof(EmailOptions.Host));
            }

            if (string.IsNullOrWhiteSpace(options.FromAddress))
            {
                missingFields.Add(nameof(EmailOptions.FromAddress));
            }

            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                missingFields.Add(nameof(EmailOptions.UserName));
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                missingFields.Add(nameof(EmailOptions.Password));
            }

            if (missingFields.Count > 0)
            {
                _logger.LogWarning(
                    "Email sending is not configured. Missing required fields: {MissingFields}.",
                    string.Join(", ", missingFields));
                return false;
            }

            if (options.Port is <= 0 or > 65535)
            {
                _logger.LogWarning("Email sending is not configured. Invalid SMTP port value: {Port}.", options.Port);
                return false;
            }

            if (!IsValidSmtpHost(options.Host))
            {
                _logger.LogWarning(
                    "Email sending is not configured correctly. SMTP host format is invalid: {Host}. Use only a hostname such as smtp.gmail.com.",
                    options.Host);
                return false;
            }

            if (!MailboxAddress.TryParse(options.FromAddress, out var parsedFromAddress) || parsedFromAddress is null)
            {
                _logger.LogWarning("Email sending is not configured correctly. Sender email format is invalid.");
                return false;
            }

            if (IsGmailHost(options.Host))
            {
                if (!options.EnableSsl)
                {
                    _logger.LogWarning(
                        "Email sending is not configured correctly for Gmail. Set Email:EnableSsl=true so STARTTLS or implicit TLS is used.");
                    return false;
                }

                if (options.Port != 587 && options.Port != 465)
                {
                    _logger.LogWarning(
                        "Gmail SMTP is usually configured with port 587 (STARTTLS) or 465 (SSL). Current port is {Port}.",
                        options.Port);
                }

                if (!LooksLikeGmailAppPassword(options.Password!))
                {
                    _logger.LogWarning(
                        "Gmail SMTP password does not match expected App Password format. Gmail rejects regular account passwords with 535 5.7.8.");
                }

                if (!string.Equals(options.UserName, parsedFromAddress.Address, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Gmail SMTP UserName and FromAddress differ. Ensure the sender address is authorized for the authenticated account.");
                }
            }

            fromAddress = parsedFromAddress;
            return true;
        }

        private static EmailOptions BuildEffectiveOptions(EmailOptions configuredOptions)
        {
            var host = FirstNonEmpty(
                configuredOptions.Host,
                GetEnvironmentValue("EMAIL_HOST", "EMAIL_SMTP_HOST", "SMTP_HOST"));

            var fromAddress = FirstNonEmpty(
                configuredOptions.FromAddress,
                GetEnvironmentValue("EMAIL_FROM_ADDRESS", "EMAIL_SMTP_FROM_ADDRESS", "SMTP_FROM_ADDRESS", "SMTP_FROM"));

            var fromName = FirstNonEmpty(
                configuredOptions.FromName,
                GetEnvironmentValue("EMAIL_FROM_NAME", "EMAIL_SMTP_FROM_NAME", "SMTP_FROM_NAME"));

            var userName = FirstNonEmpty(
                configuredOptions.UserName,
                GetEnvironmentValue("EMAIL_USERNAME", "EMAIL_SMTP_USERNAME", "EMAIL_SMTP_USER", "SMTP_USERNAME", "SMTP_USER"));

            var password = NormalizePassword(
                FirstNonEmpty(
                    configuredOptions.Password,
                    GetEnvironmentValue("EMAIL_PASSWORD", "EMAIL_SMTP_PASSWORD", "SMTP_PASSWORD")),
                host);

            var configuredPortText = configuredOptions.Port > 0
                ? configuredOptions.Port.ToString()
                : null;
            var portText = FirstNonEmpty(
                GetEnvironmentValue("EMAIL_PORT", "EMAIL_SMTP_PORT", "SMTP_PORT"),
                configuredPortText);
            var port = string.IsNullOrWhiteSpace(portText)
                ? 587
                : ParseIntOrDefault(portText, defaultValue: -1);

            var enableSsl = ParseBoolOrDefault(
                FirstNonEmpty(
                    GetEnvironmentValue("EMAIL_ENABLE_SSL", "EMAIL_SMTP_ENABLE_SSL", "SMTP_ENABLE_SSL"),
                    configuredOptions.EnableSsl.ToString()),
                defaultValue: true);

            return new EmailOptions
            {
                Host = host,
                FromAddress = fromAddress,
                FromName = fromName,
                UserName = userName,
                Password = password,
                Port = port,
                EnableSsl = enableSsl
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var normalized = NormalizeConfigValue(value);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            return null;
        }

        private static string? GetEnvironmentValue(params string[] variableNames)
        {
            foreach (var variableName in variableNames)
            {
                var value = Environment.GetEnvironmentVariable(variableName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string? NormalizeConfigValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            normalized = StripWrappingQuotes(normalized);

            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : normalized;
        }

        private static string StripWrappingQuotes(string value)
        {
            while (value.Length >= 2 &&
                   ((value[0] == '"' && value[^1] == '"') ||
                    (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1].Trim();
            }

            return value;
        }

        private static string? NormalizePassword(string? value, string? host)
        {
            var normalizedPassword = NormalizeConfigValue(value);
            if (normalizedPassword is null || !IsGmailHost(host))
            {
                return normalizedPassword;
            }

            var compactPassword = RemoveWhitespace(normalizedPassword);
            return LooksLikeGmailAppPassword(compactPassword)
                ? compactPassword
                : normalizedPassword;
        }

        private static string RemoveWhitespace(string value)
        {
            return new string(value.Where(static c => !char.IsWhiteSpace(c)).ToArray());
        }

        private static bool LooksLikeGmailAppPassword(string password)
        {
            var compactPassword = RemoveWhitespace(password);
            return compactPassword.Length == 16 && compactPassword.All(char.IsLetterOrDigit);
        }

        private static bool IsGmailHost(string? host)
        {
            return string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidSmtpHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return Uri.CheckHostName(host) != UriHostNameType.Unknown;
        }

        private static string ToStageText(EmailSendStage stage)
        {
            return stage switch
            {
                EmailSendStage.Connect => "connect",
                EmailSendStage.Authenticate => "authenticate",
                EmailSendStage.Send => "send",
                EmailSendStage.Disconnect => "disconnect",
                _ => "unknown"
            };
        }

        private static int ParseIntOrDefault(string? value, int defaultValue)
        {
            return int.TryParse(value, out var parsedValue) ? parsedValue : defaultValue;
        }

        private static bool ParseBoolOrDefault(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (bool.TryParse(value, out var parsedValue))
            {
                return parsedValue;
            }

            return value.Trim() switch
            {
                "1" => true,
                "0" => false,
                _ => defaultValue
            };
        }

        private static SecureSocketOptions ResolveSecureSocketOptions(int port, bool enableSsl)
        {
            if (!enableSsl)
            {
                return SecureSocketOptions.None;
            }

            // Port 465 expects implicit TLS; other SSL-enabled ports typically use STARTTLS.
            return port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
        }
    }
}
