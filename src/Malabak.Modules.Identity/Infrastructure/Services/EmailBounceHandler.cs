using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public enum BounceResult
{
    Applied = 0,
    Ignored = 1,
    InvalidSignature = 2
}

/// <summary>
/// Provider-neutral bounce/complaint webhook: the body must carry an HMAC-SHA256 (hex) signature made with
/// Email:WebhookSecret. Hard bounces and complaints suppress the address; soft bounces are ignored.
/// </summary>
public sealed class EmailBounceHandler
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<EmailBounceHandler> _logger;
    private readonly byte[]? _secret;

    public EmailBounceHandler(IdentityDbContext dbContext, IClock clock, IConfiguration configuration, ILogger<EmailBounceHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        var secret = configuration["Email:WebhookSecret"];
        _secret = string.IsNullOrWhiteSpace(secret) ? null : Encoding.UTF8.GetBytes(secret);
    }

    public bool IsValidSignature(string rawBody, string? signatureHex)
    {
        // No secret configured = webhook disabled (fail closed).
        if (_secret is null || string.IsNullOrWhiteSpace(signatureHex))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Convert.ToHexString(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signatureHex.Trim().ToLowerInvariant()));
    }

    public async Task<BounceResult> ApplyAsync(string rawBody, string? signatureHex, string email, string type, CancellationToken cancellationToken = default)
    {
        if (!IsValidSignature(rawBody, signatureHex))
        {
            _logger.LogWarning("Rejected email bounce webhook with an invalid signature");
            return BounceResult.InvalidSignature;
        }

        EmailSuppressionReason? reason = type?.ToLowerInvariant() switch
        {
            "hard" or "hard_bounce" or "permanent" => EmailSuppressionReason.HardBounce,
            "complaint" or "spam" => EmailSuppressionReason.Complaint,
            _ => null
        };

        if (reason is null || string.IsNullOrWhiteSpace(email))
        {
            return BounceResult.Ignored;
        }

        var hash = SmtpEmailSender.HashAddress(email);
        if (!await _dbContext.EmailSuppressions.AnyAsync(s => s.EmailHash == hash, cancellationToken))
        {
            _dbContext.EmailSuppressions.Add(EmailSuppression.Create(hash, reason.Value, _clock.UtcNow));
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Email address suppressed ({Reason})", reason);
        }

        return BounceResult.Applied;
    }
}
