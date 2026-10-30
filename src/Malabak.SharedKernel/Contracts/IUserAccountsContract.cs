namespace Malabak.SharedKernel.Contracts;

/// <summary>Identity-module operations other modules need, without touching Identity's entities or DbContext.</summary>
public interface IUserAccountsContract
{
    /// <summary>E.164 form of an Egyptian phone number, or null when it isn't valid.</summary>
    string? NormalizePhone(string phoneNumber);

    /// <summary>True when the user's verified email or phone equals the given one (invitations are bound to an identity).</summary>
    Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default);

    /// <summary>Display names for the given users (deleted or unknown users are left out), e.g. for a venue's staff list.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);

    /// <summary>Sends the staff invitation link (email when given, otherwise SMS). The raw token is never logged.</summary>
    Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
        CancellationToken cancellationToken = default);
}
