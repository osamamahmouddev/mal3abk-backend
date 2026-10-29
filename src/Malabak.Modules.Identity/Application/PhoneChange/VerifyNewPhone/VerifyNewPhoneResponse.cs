namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;

public sealed record VerifyNewPhoneResponse(
    bool Success,
    string NewPhoneNumber,
    string RecoveryToken);
