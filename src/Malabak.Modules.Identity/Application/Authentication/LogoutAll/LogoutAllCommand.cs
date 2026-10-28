using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId) : ICommand<Result>;
