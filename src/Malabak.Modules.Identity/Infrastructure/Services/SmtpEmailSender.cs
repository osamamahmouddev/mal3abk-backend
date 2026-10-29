using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// SMTP sender (Mailpit on localhost:1025 locally; any SMTP relay such as SES/SendGrid/Postmark in production via
/// Email:Smtp:*). Sends text + HTML, uses TLS when configured, and skips suppressed (bounced/complained) addresses.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _enableSsl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly MailAddress _from;

    public SmtpEmailSender(IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<SmtpEmailSender> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _host = configuration["Email:Smtp:Host"] ?? "localhost";
        _port = int.TryParse(configuration["Email:Smtp:Port"], out var port) ? port : 1025;
        _enableSsl = bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var ssl) && ssl;
        _username = configuration["Email:Smtp:Username"];
        _password = configuration["Email:Smtp:Password"];
        _from = new MailAddress(configuration["Email:From"] ?? "no-reply@malabak.local", configuration["Email:FromName"] ?? "Malabak | ملعبك");
    }

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default) =>
        SendCoreAsync(toAddress, subject, body, html: null, cancellationToken);

    public Task SendAsync(string toAddress, RenderedEmail email, CancellationToken cancellationToken = default) =>
        SendCoreAsync(toAddress, email.Subject, email.Text, email.Html, cancellationToken);

    public static string HashAddress(string address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant())));

    private async Task SendCoreAsync(string toAddress, string subject, string text, string? html, CancellationToken cancellationToken)
    {
        if (await IsSuppressedAsync(toAddress, cancellationToken))
        {
            _logger.LogWarning("Email not sent: address is on the suppression list ({Subject})", subject);
            return;
        }

        using var message = new MailMessage { From = _from, Subject = subject, Body = text, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        message.To.Add(toAddress);
        if (html is not null)
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using var client = new SmtpClient(_host, _port) { EnableSsl = _enableSsl };
        if (!string.IsNullOrWhiteSpace(_username))
        {
            client.Credentials = new NetworkCredential(_username, _password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }

    private async Task<bool> IsSuppressedAsync(string toAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hash = HashAddress(toAddress);
            return await db.EmailSuppressions.AnyAsync(s => s.EmailHash == hash, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return false; // no database (e.g. tooling); suppression is best-effort, delivery still matters
        }
    }
}
