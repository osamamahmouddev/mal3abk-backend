using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Common;

public sealed record FirebaseTokenClaims(
    string Uid,
    string PhoneNumber,
    string? Email,
    string? Name);

public interface IFirebaseAuthService
{
    Task<Result<FirebaseTokenClaims>> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default);
}
