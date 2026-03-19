using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Vizora.Services;

namespace Vizora.Tests.Services;

[Collection("EmailConfigTests")]
public class AccountEmailServiceTests
{
    [Fact]
    public async Task SendEmailAsync_WhenRequiredConfigMissing_ReturnsFalseAndLogsMissingFields()
    {
        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(new EmailOptions(), logger);

        var sent = await service.SendEmailAsync("user@example.com", "subject", "body");

        Assert.False(sent);
        Assert.Contains(logger.Messages, message => message.Contains("Missing required fields", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_UsesEnvironmentAliasesAndFromAddressFallbackForUsername()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["EMAIL_HOST"] = "smtp.example.local",
            ["EMAIL_FROM_ADDRESS"] = "noreply@example.local",
            ["EMAIL_PASSWORD"] = "placeholder-password",
            ["EMAIL_USERNAME"] = null
        });

        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(new EmailOptions(), logger);

        var sent = await service.SendEmailAsync(" ", "subject", "body");

        Assert.False(sent);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Missing required fields", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("invalid recipient email format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenHostContainsScheme_ReturnsFalseAndLogsInvalidHost()
    {
        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(
            new EmailOptions
            {
                Host = "smtp://smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                FromAddress = "noreply@gmail.com",
                UserName = "noreply@gmail.com",
                Password = "abcdefghijklmnop"
            },
            logger);

        var sent = await service.SendEmailAsync("user@example.com", "subject", "body");

        Assert.False(sent);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("SMTP host format is invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenGmailPasswordLooksLikeAccountPassword_LogsAppPasswordGuidance()
    {
        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(
            new EmailOptions
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                FromAddress = "noreply@gmail.com",
                UserName = "noreply@gmail.com",
                Password = "not-an-app-password"
            },
            logger);

        var sent = await service.SendEmailAsync(" ", "subject", "body");

        Assert.False(sent);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("expected App Password format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenGmailPasswordContainsGroups_NormalizesAndAvoidsFalseWarning()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["EMAIL_HOST"] = "\"smtp.gmail.com\"",
            ["EMAIL_FROM_ADDRESS"] = "\"noreply@gmail.com\"",
            ["EMAIL_PASSWORD"] = "\"abcd efgh ijkl mnop\"",
            ["EMAIL_USERNAME"] = null
        });

        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(new EmailOptions(), logger);

        var sent = await service.SendEmailAsync(" ", "subject", "body");

        Assert.False(sent);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("expected App Password format", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("invalid recipient email format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenAuthenticationFails_LogsAuthenticateStageAndReturnsFalse()
    {
        var logger = new CapturingLogger<AccountEmailService>();
        var smtpFactory = new StaticSmtpClientFactory(() => new DelegatingSmtpClient
        {
            OnAuthenticate = static (_, _, _) =>
                throw new MailKit.Security.AuthenticationException("535 5.7.8 Username and Password not accepted.")
        });

        var service = CreateService(
            new EmailOptions
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                FromAddress = "noreply@gmail.com",
                UserName = "noreply@gmail.com",
                Password = "abcdefghijklmnop"
            },
            logger,
            smtpFactory);

        var sent = await service.SendEmailAsync("user@example.com", "subject", "body");

        Assert.False(sent);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("authenticate stage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("App Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenSendFailsAfterAuthentication_LogsSendStageAndReturnsFalse()
    {
        var logger = new CapturingLogger<AccountEmailService>();
        var smtpFactory = new StaticSmtpClientFactory(() => new DelegatingSmtpClient
        {
            OnSend = static (_, _) =>
                throw new SmtpCommandException(
                    SmtpErrorCode.MessageNotAccepted,
                    SmtpStatusCode.TransactionFailed,
                    "Simulated send failure.")
        });

        var service = CreateService(
            new EmailOptions
            {
                Host = "smtp.example.local",
                Port = 587,
                EnableSsl = true,
                FromAddress = "noreply@example.local",
                UserName = "noreply@example.local",
                Password = "placeholder-password"
            },
            logger,
            smtpFactory);

        var sent = await service.SendEmailAsync("user@example.com", "subject", "body");

        Assert.False(sent);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("during send stage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendEmailAsync_WhenEnvironmentPortIsInvalid_LogsPortValidationFailure()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["EMAIL_HOST"] = "smtp.example.local",
            ["EMAIL_FROM_ADDRESS"] = "noreply@example.local",
            ["EMAIL_PASSWORD"] = "placeholder-password",
            ["EMAIL_PORT"] = "\"invalid-port\""
        });

        var logger = new CapturingLogger<AccountEmailService>();
        var service = CreateService(new EmailOptions(), logger);

        var sent = await service.SendEmailAsync("user@example.com", "subject", "body");

        Assert.False(sent);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("Invalid SMTP port value", StringComparison.OrdinalIgnoreCase));
    }

    private static AccountEmailService CreateService(
        EmailOptions options,
        ILogger<AccountEmailService> logger,
        IAccountSmtpClientFactory? smtpClientFactory = null)
    {
        return new AccountEmailService(
            new StaticOptionsSnapshot<EmailOptions>(options),
            smtpClientFactory ?? new StaticSmtpClientFactory(),
            logger);
    }

    private sealed class StaticOptionsSnapshot<TOptions> : IOptionsSnapshot<TOptions>
        where TOptions : class
    {
        public StaticOptionsSnapshot(TOptions value)
        {
            Value = value;
        }

        public TOptions Value { get; }

        public TOptions Get(string? name)
        {
            return Value;
        }
    }

    private sealed class StaticSmtpClientFactory : IAccountSmtpClientFactory
    {
        private readonly Func<IAccountSmtpClient> _factory;

        public StaticSmtpClientFactory(Func<IAccountSmtpClient>? factory = null)
        {
            _factory = factory ?? (() => new DelegatingSmtpClient());
        }

        public IAccountSmtpClient Create()
        {
            return _factory();
        }
    }

    private sealed class DelegatingSmtpClient : IAccountSmtpClient
    {
        public Func<string, int, SecureSocketOptions, CancellationToken, Task>? OnConnect { get; init; }

        public Func<string, string, CancellationToken, Task>? OnAuthenticate { get; init; }

        public Func<MimeMessage, CancellationToken, Task>? OnSend { get; init; }

        public Func<bool, CancellationToken, Task>? OnDisconnect { get; init; }

        public Task ConnectAsync(
            string host,
            int port,
            SecureSocketOptions secureSocketOptions,
            CancellationToken cancellationToken)
        {
            return OnConnect?.Invoke(host, port, secureSocketOptions, cancellationToken) ?? Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            return OnAuthenticate?.Invoke(userName, password, cancellationToken) ?? Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            return OnSend?.Invoke(message, cancellationToken) ?? Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            return OnDisconnect?.Invoke(quit, cancellationToken) ?? Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger<TCategoryName> : ILogger<TCategoryName>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(Dictionary<string, string?> values)
        {
            foreach (var (key, value) in values)
            {
                _originalValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

[CollectionDefinition("EmailConfigTests", DisableParallelization = true)]
public sealed class EmailConfigTestsCollection
{
}
