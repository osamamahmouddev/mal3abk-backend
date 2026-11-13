using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Posts account emails to a Slack channel instead of sending them, for testing before a mail provider is set up.
/// The recipient is masked in the message; the body (with its link) is shown because testers need it, which is why
/// the provider is refused in Production. Only the message kind ("email") is logged.
/// </summary>
public sealed class SlackEmailSender : IEmailSender
{
    private readonly SlackWebhookClient _slack;
    private readonly EmailDeliveryOptions _options;
    private readonly IHostEnvironment _environment;

    public SlackEmailSender(SlackWebhookClient slack, IOptions<EmailDeliveryOptions> options, IHostEnvironment environment)
    {
        _slack = slack;
        _options = options.Value;
        _environment = environment;
    }

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default) =>
        _slack.PostToAsync(_options.Slack.WebhookUrl, BuildMessage(toAddress, subject, body, _environment.EnvironmentName), "email", cancellationToken);

    public Task SendAsync(string toAddress, RenderedEmail email, CancellationToken cancellationToken = default) =>
        SendAsync(toAddress, email.Subject, email.Text, cancellationToken);

    public static string BuildMessage(string toAddress, string subject, string body, string environmentName) =>
        $"""
        *ملعبك — إيميل* ({environmentName})
        إلى: `{MaskEmail(toAddress)}`
        الموضوع: {subject}

        {body}
        """;

    /// <summary>Keeps the first character of the local part and the domain: owner@gmail.com → o••••@gmail.com.</summary>
    public static string MaskEmail(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0
            ? new string('•', address.Length)
            : string.Concat(address.AsSpan(0, 1), new string('•', Math.Max(at - 1, 1)), address.AsSpan(at));
    }
}
