using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Teams.Application.Common;

/// <summary>Adds display names to rosters and join requests; Identity stays the only module that reads users.</summary>
public static class PlayerNames
{
    public static async Task<IReadOnlyList<TeamMemberDto>> WithNamesAsync(
        this IReadOnlyList<TeamMemberDto> members, IUserAccountsContract? accounts, CancellationToken cancellationToken)
    {
        if (accounts is null || members.Count == 0)
        {
            return members;
        }

        var names = await accounts.GetFullNamesAsync(members.Select(m => m.PlayerId).Distinct().ToList(), cancellationToken);
        return members.Select(m => m with { PlayerName = names.GetValueOrDefault(m.PlayerId) }).ToList();
    }

    public static async Task<IReadOnlyList<TeamJoinRequestDto>> WithNamesAsync(
        this IReadOnlyList<TeamJoinRequestDto> requests, IUserAccountsContract? accounts, CancellationToken cancellationToken)
    {
        if (accounts is null || requests.Count == 0)
        {
            return requests;
        }

        var names = await accounts.GetFullNamesAsync(requests.Select(r => r.PlayerId).Distinct().ToList(), cancellationToken);
        return requests.Select(r => r with { PlayerName = names.GetValueOrDefault(r.PlayerId) }).ToList();
    }
}
