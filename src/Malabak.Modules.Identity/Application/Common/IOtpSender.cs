namespace Malabak.Modules.Identity.Application.Common;

/// <summary>Why a one-time code was issued; shown to the recipient and never interchangeable.</summary>
public enum OtpPurpose
{
    Login,
    ChangePhoneOld,
    ChangePhoneNew,

    /// <summary>Re-authentication before a sensitive action (SECURITY.md §3.8).</summary>
    StepUp,
}

/// <summary>
/// Delivers a one-time code to the owner of a phone number. The code must never be logged
/// (SECURITY.md §4.3, §13); implementations only report whether delivery succeeded.
/// </summary>
public interface IOtpSender
{
    Task<bool> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default);
}
