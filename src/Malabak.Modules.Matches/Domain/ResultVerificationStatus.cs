namespace Malabak.Modules.Matches.Domain;

public enum ResultVerificationStatus
{
    None = 0,
    PendingVerification = 1,
    Confirmed = 2,
    Disputed = 3,
    AutoConfirmed = 4
}
