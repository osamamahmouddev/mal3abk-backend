using System.Security.Claims;
using Malabak.Modules.Identity.Domain;

namespace Malabak.Modules.Identity.Application.Common;

public interface ITokenService
{
    TokenResult GenerateTokens(User user);

    string GenerateRawRefreshToken();

    string HashRefreshToken(string rawRefreshToken);

    ClaimsPrincipal? ValidateAccessToken(string token);
}
