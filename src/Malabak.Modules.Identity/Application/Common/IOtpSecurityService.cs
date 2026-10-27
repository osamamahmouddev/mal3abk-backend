namespace Malabak.Modules.Identity.Application.Common;

public interface IOtpSecurityService
{
    string GenerateSecureOtpCode();
    string ComputeHmacHash(string code);
    bool VerifyCode(string candidateCode, string storedHmacHash);
}
