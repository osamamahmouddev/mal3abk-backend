namespace Malabak.Modules.Identity.Application.Common;

public enum SecurityAlertKind
{
    /// <summary>Sent to the old number after a phone change, with a 24-hour recovery link (SECURITY.md §5.5).</summary>
    PhoneChanged,

    /// <summary>Sent to the restored number after a recovery link was used.</summary>
    PhoneRestored,
}

/// <summary>
/// The content of a security alert; each sender renders it for its channel. <see cref="RecoveryLink"/> carries a
/// live recovery token and <see cref="NewPhoneNumber"/> is PII, so only <see cref="Kind"/> may be logged
/// (SECURITY.md §15.5). <see cref="ToString"/> prints the kind alone in case an instance reaches a log line.
/// </summary>
public sealed record SecurityAlert
{
    private SecurityAlert(SecurityAlertKind kind, string? newPhoneNumber, string? recoveryLink)
    {
        Kind = kind;
        NewPhoneNumber = newPhoneNumber;
        RecoveryLink = recoveryLink;
    }

    public SecurityAlertKind Kind { get; }

    public string? NewPhoneNumber { get; }

    public string? RecoveryLink { get; }

    public static SecurityAlert PhoneChanged(string newPhoneNumber, string recoveryLink) =>
        new(SecurityAlertKind.PhoneChanged, newPhoneNumber, recoveryLink);

    public static SecurityAlert PhoneRestored() => new(SecurityAlertKind.PhoneRestored, null, null);

    public override string ToString() => $"{nameof(SecurityAlert)} {{ {nameof(Kind)} = {Kind} }}";
}

/// <summary>
/// Notifies the owner of a phone number about a security-relevant change to their account. Alerts are sent after
/// the change is committed, so a failed delivery is reported by the sender and never fails the request.
/// Implementations must not log the phone number or the alert content.
/// </summary>
public interface ISmsAlertService
{
    Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default);
}
