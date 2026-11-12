using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange.CancelPhoneChange;

public sealed record CancelPhoneChangeCommand(
    Guid UserId,
    Guid RequestId) : ICommand<Result>;
