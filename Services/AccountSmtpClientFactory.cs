using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Vizora.Services
{
    public interface IAccountSmtpClient : IDisposable
    {
        Task ConnectAsync(string host, int port, SecureSocketOptions secureSocketOptions, CancellationToken cancellationToken);

        Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

        Task SendAsync(MimeMessage message, CancellationToken cancellationToken);

        Task DisconnectAsync(bool quit, CancellationToken cancellationToken);
    }

    public interface IAccountSmtpClientFactory
    {
        IAccountSmtpClient Create();
    }

    public sealed class MailKitAccountSmtpClientFactory : IAccountSmtpClientFactory
    {
        public IAccountSmtpClient Create()
        {
            return new MailKitAccountSmtpClient();
        }

        private sealed class MailKitAccountSmtpClient : IAccountSmtpClient
        {
            private readonly SmtpClient _client = new();

            public Task ConnectAsync(
                string host,
                int port,
                SecureSocketOptions secureSocketOptions,
                CancellationToken cancellationToken)
            {
                return _client.ConnectAsync(host, port, secureSocketOptions, cancellationToken);
            }

            public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
            {
                return _client.AuthenticateAsync(userName, password, cancellationToken);
            }

            public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
            {
                return _client.SendAsync(message, cancellationToken);
            }

            public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
            {
                return _client.DisconnectAsync(quit, cancellationToken);
            }

            public void Dispose()
            {
                _client.Dispose();
            }
        }
    }
}
