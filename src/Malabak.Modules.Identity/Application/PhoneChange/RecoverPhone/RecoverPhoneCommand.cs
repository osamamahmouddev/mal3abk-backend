using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;

public sealed record RecoverPhoneCommand(string RecoveryToken) : ICommand<Result>;
