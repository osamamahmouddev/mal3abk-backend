using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class OtpSecurityService : IOtpSecurityService
{
    private readonly byte[] _hmacKey;

    public OtpSecurityService(IConfiguration configuration)
    {
        var secret = configuration["Security:Otp:HmacSecret"] ?? "Malabak-Secure-Otp-HMAC-SHA256-Key-2026-Egypt!";
        _hmacKey = Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateSecureOtpCode()
    {
        // 6-digit CSPRNG integer in range [100000, 999999]
        var codeNumber = RandomNumberGenerator.GetInt32(100000, 1000000);
        return codeNumber.ToString("D6");
    }

    public string ComputeHmacHash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using var hmac = new HMACSHA256(_hmacKey);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(code.Trim()));
        return Convert.ToHexString(hashBytes);
    }

    public bool VerifyCode(string candidateCode, string storedHmacHash)
    {
        if (string.IsNullOrWhiteSpace(candidateCode) || string.IsNullOrWhiteSpace(storedHmacHash))
        {
            return false;
        }

        var candidateHash = ComputeHmacHash(candidateCode);
        var candidateBytes = Encoding.UTF8.GetBytes(candidateHash);
        var storedBytes = Encoding.UTF8.GetBytes(storedHmacHash);

        return CryptographicOperations.FixedTimeEquals(candidateBytes, storedBytes);
    }
}
