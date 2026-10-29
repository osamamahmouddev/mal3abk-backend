namespace Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;

public sealed record StartPhoneChangeResponse(
    Guid RequestId,
    DateTime ExpiresAtUtc);
