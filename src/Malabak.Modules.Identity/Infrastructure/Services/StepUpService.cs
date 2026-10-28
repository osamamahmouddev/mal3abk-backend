using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class StepUpService : IStepUpService
{
    private readonly byte[] _hmacKey;
    private readonly IClock _clock;

    public StepUpService(IConfiguration configuration, IClock clock)
    {
        var secret = configuration["Security:StepUp:SecretKey"] ?? "Malabak-StepUp-Verification-HMAC-SHA256-Key-2026!";
        _hmacKey = Encoding.UTF8.GetBytes(secret);
        _clock = clock;
    }

    public string IssueStepUpToken(Guid userId, string scope, TimeSpan? validFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var nowUtc = _clock.UtcNow;
        var issuedUnix = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
        var payload = $"{userId:N}:{scope.Trim().ToLowerInvariant()}:{issuedUnix}";

        using var hmac = new HMACSHA256(_hmacKey);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signatureHex = Convert.ToHexString(signatureBytes).ToLowerInvariant();

        return $"{payload}:{signatureHex}";
    }

    public bool ValidateStepUpToken(
        string stepUpToken,
        Guid expectedUserId,
        string expectedScope,
        TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(stepUpToken))
        {
            return false;
        }

        var parts = stepUpToken.Split(':');
        if (parts.Length != 4)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out var tokenUserId) || tokenUserId != expectedUserId)
        {
            return false;
        }

        var tokenScope = parts[1];
        if (!string.Equals(tokenScope, expectedScope.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!long.TryParse(parts[2], out var issuedUnix))
        {
            return false;
        }

        var payload = $"{parts[0]}:{parts[1]}:{parts[2]}";
        using var hmac = new HMACSHA256(_hmacKey);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignatureHex = Convert.ToHexString(expectedSignatureBytes).ToLowerInvariant();

        var providedSignatureHex = parts[3].ToLowerInvariant();
        var providedBytes = Encoding.UTF8.GetBytes(providedSignatureHex);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignatureHex);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return false;
        }

        var issuedTime = DateTimeOffset.FromUnixTimeSeconds(issuedUnix).UtcDateTime;
        var nowUtc = _clock.UtcNow;
        var allowedMaxAge = maxAge ?? TimeSpan.FromMinutes(5);

        // Disallow tokens from the future beyond 1 minute skew, and ensure token is within maxAge (<= 5 mins)
        if (issuedTime > nowUtc.AddMinutes(1))
        {
            return false;
        }

        return nowUtc - issuedTime <= allowedMaxAge;
    }
}
