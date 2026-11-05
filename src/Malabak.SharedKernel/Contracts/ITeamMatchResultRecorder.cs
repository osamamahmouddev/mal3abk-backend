namespace Malabak.SharedKernel.Contracts;

public interface ITeamMatchResultRecorder
{
    Task RecordConfirmedResultAsync(
        Guid homeTeamId,
        Guid awayTeamId,
        int homeScore,
        int awayScore,
        CancellationToken cancellationToken = default);
}
