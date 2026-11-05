using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain;

public sealed class MatchResultAuditRecord : Entity<Guid>
{
    public Guid MatchId { get; private set; }
    public int? PreviousHomeScore { get; private set; }
    public int? PreviousAwayScore { get; private set; }
    public int? NewHomeScore { get; private set; }
    public int? NewAwayScore { get; private set; }
    public ResultVerificationStatus PreviousStatus { get; private set; }
    public ResultVerificationStatus NewStatus { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public string ChangeType { get; private set; } = string.Empty;
    public string? Reason { get; private set; }

    private MatchResultAuditRecord()
    {
    }

    public static MatchResultAuditRecord Create(
        Guid matchId,
        int? previousHomeScore,
        int? previousAwayScore,
        int? newHomeScore,
        int? newAwayScore,
        ResultVerificationStatus previousStatus,
        ResultVerificationStatus newStatus,
        Guid changedByUserId,
        string changeType,
        string? reason,
        DateTime nowUtc)
    {
        return new MatchResultAuditRecord
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PreviousHomeScore = previousHomeScore,
            PreviousAwayScore = previousAwayScore,
            NewHomeScore = newHomeScore,
            NewAwayScore = newAwayScore,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedByUserId = changedByUserId,
            ChangeType = changeType,
            Reason = reason?.Trim(),
            CreatedAtUtc = nowUtc
        };
    }
}
