using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Result>;
