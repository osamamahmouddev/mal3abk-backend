using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;

public sealed record VerifyOldPhoneCommand(
    Guid UserId,
    Guid RequestId,
    string Code) : ICommand<Result>;
