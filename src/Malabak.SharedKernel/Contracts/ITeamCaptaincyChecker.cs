namespace Malabak.SharedKernel.Contracts;

public interface ITeamCaptaincyChecker
{
    Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default);
    Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default);
}
