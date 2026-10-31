using Malabak.SharedKernel.Contracts;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>User accounts stand-in for handler tests: every user is called "Player".</summary>
internal sealed class FakeUserAccounts : IUserAccountsContract
{
    public string? NormalizePhone(string phoneNumber) => phoneNumber;

    public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Player"));

    public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
