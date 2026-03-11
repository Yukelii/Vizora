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
        private readonly ILogger<AccountEmailService> _logger;

        public AccountEmailService(IOptionsSnapshot<EmailOptions> emailOptions, ILogger<AccountEmailService> logger)
        {
            _emailOptions = emailOptions;
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

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = new SmtpClient();
                var secureSocketOptions = ResolveSecureSocketOptions(options.Port, options.EnableSsl);

                // Connect and authenticate explicitly because most providers require SMTP auth.
                await client.ConnectAsync(
                    options.Host,
                    options.Port,
                    secureSocketOptions,
                    cancellationToken);

                await client.AuthenticateAsync(
                    options.UserName,
                    options.Password,
                    cancellationToken);

                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                _logger.LogWarning(ex, "SMTP authentication failed for host {Host}:{Port}.", options.Host, options.Port);
                return false;
            }
            catch (SmtpCommandException ex)
            {
                _logger.LogWarning(
                    ex,
                    "SMTP command failed for host {Host}:{Port} with status code {StatusCode}.",
                    options.Host,
                    options.Port,
                    ex.StatusCode);
                return false;
            }
            catch (SmtpProtocolException ex)
            {
                _logger.LogWarning(ex, "SMTP protocol failure for host {Host}:{Port}.", options.Host, options.Port);
                return false;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "SMTP connection failure for host {Host}:{Port}.", options.Host, options.Port);
                return false;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is FormatException ||
                ex is System.Security.Authentication.AuthenticationException)
            {
                _logger.LogWarning(ex, "Failed to send account lifecycle email.");
                return false;
            }
        }

        private bool TryGetValidatedOptions(out EmailOptions options, out MailboxAddress fromAddress)
        {
            options = _emailOptions.Value;
            fromAddress = default!;

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

            if (!MailboxAddress.TryParse(options.FromAddress, out var parsedFromAddress) || parsedFromAddress is null)
            {
                _logger.LogWarning("Email sending is not configured correctly. Sender email format is invalid.");
                return false;
            }

            fromAddress = parsedFromAddress;
            return true;
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
