using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;

public sealed record VerifyNewPhoneCommand(
    Guid UserId,
    Guid RequestId,
    string Code) : ICommand<Result<VerifyNewPhoneResponse>>;
