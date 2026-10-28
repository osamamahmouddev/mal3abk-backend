using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Have I Been Pwned range API with k-anonymity: only the first 5 hex chars of the SHA-1 leave the server
/// and padding hides which suffix we were looking for. If the API is unreachable the check is skipped
/// (logged) rather than blocking every registration — the length and common-password rules still apply.
/// </summary>
public sealed class HibpBreachedPasswordChecker : IBreachedPasswordChecker
{
    private const string _rangeEndpoint = "https://api.pwnedpasswords.com/range/";
    private static readonly HttpClient _httpClient = CreateClient();

    private readonly ILogger<HibpBreachedPasswordChecker> _logger;

    public HibpBreachedPasswordChecker(ILogger<HibpBreachedPasswordChecker> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
    {
        // SHA-1 is required by the HIBP protocol; it's used for lookup only, never for storing passwords.
#pragma warning disable CA5350
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = sha1[..5];
        var suffix = sha1[5..];

        try
        {
            var body = await _httpClient.GetStringAsync(_rangeEndpoint + prefix, cancellationToken);
            return IsSuffixListed(body, suffix);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Breached-password check unavailable; continuing without it");
            return false;
        }
    }

    internal static bool IsSuffixListed(string rangeResponse, string suffix)
    {
        foreach (var line in rangeResponse.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0
                && line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(separator + 1).Trim(), out var count) && count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.Add("Add-Padding", "true");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Malabak-Api");
        return client;
    }
}
