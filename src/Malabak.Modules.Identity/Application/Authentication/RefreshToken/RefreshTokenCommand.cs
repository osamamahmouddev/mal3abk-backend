using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<Result<RefreshTokenResponse>>;
