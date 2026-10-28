using System.Net.Mail;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Minimal SMTP sender (Mailpit on localhost:1025 in docker-compose). The provider abstraction with
/// templates, retries and a real provider is TASK-204; this keeps the flows working until then.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _from;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _host = configuration["Email:Smtp:Host"] ?? "localhost";
        _port = int.TryParse(configuration["Email:Smtp:Port"], out var port) ? port : 1025;
        _from = configuration["Email:From"] ?? "no-reply@malabak.local";
    }

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage(_from, toAddress, subject, body);
        using var client = new SmtpClient(_host, _port);
        await client.SendMailAsync(message, cancellationToken);
    }
}
