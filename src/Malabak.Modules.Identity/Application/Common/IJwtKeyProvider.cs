using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Modules.Identity.Application.Common;

public interface IJwtKeyProvider
{
    string CurrentKeyId { get; }

    ECDsaSecurityKey GetCurrentSigningKey();

    SigningCredentials GetCurrentSigningCredentials();

    IEnumerable<SecurityKey> GetValidationKeys();
}
