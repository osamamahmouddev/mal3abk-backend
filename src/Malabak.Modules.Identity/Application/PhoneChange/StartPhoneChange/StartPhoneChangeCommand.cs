using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;

public sealed record StartPhoneChangeCommand(
    Guid UserId,
    string NewPhoneNumber,
    string StepUpToken) : ICommand<Result<StartPhoneChangeResponse>>;
