namespace Malabak.Modules.Identity.Application.Common;

public enum SecurityAlertKind
{
    /// <summary>Sent to the old number after a phone change, with a 24-hour recovery link (SECURITY.md §5.5).</summary>
    PhoneChanged,

    /// <summary>Sent to the restored number after a recovery link was used.</summary>
    PhoneRestored,

    /// <summary>Sent when the account signs in from a device it has not used before (SECURITY.md §4.3).</summary>
    NewDeviceSignIn,

    /// <summary>Sent to an invited venue staff member who has no email, with a 48-hour invitation link.</summary>
    StaffInvitation,
}

/// <summary>
/// The content of a security alert; each sender renders it for its channel. <see cref="RecoveryLink"/> and
/// <see cref="InvitationLink"/> carry live tokens and <see cref="NewPhoneNumber"/> is PII, so only <see cref="Kind"/>
/// may be logged (SECURITY.md §15.5). <see cref="ToString"/> prints the kind alone in case an instance reaches a log line.
/// </summary>
public sealed record SecurityAlert
{
    private SecurityAlert(SecurityAlertKind kind)
    {
        Kind = kind;
    }

    public SecurityAlertKind Kind { get; }

    public string? NewPhoneNumber { get; private init; }

    public string? RecoveryLink { get; private init; }

    public string? DeviceName { get; private init; }

    public DateTime? OccurredAtUtc { get; private init; }

    public string? VenueName { get; private init; }

    public string? Role { get; private init; }

    public string? InvitationLink { get; private init; }

    public static SecurityAlert PhoneChanged(string newPhoneNumber, string recoveryLink) =>
        new(SecurityAlertKind.PhoneChanged) { NewPhoneNumber = newPhoneNumber, RecoveryLink = recoveryLink };

    public static SecurityAlert PhoneRestored() => new(SecurityAlertKind.PhoneRestored);

    public static SecurityAlert NewDeviceSignIn(string deviceName, DateTime signedInAtUtc) =>
        new(SecurityAlertKind.NewDeviceSignIn) { DeviceName = deviceName, OccurredAtUtc = signedInAtUtc };

    public static SecurityAlert StaffInvitation(string venueName, string role, string invitationLink) =>
        new(SecurityAlertKind.StaffInvitation) { VenueName = venueName, Role = role, InvitationLink = invitationLink };

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
