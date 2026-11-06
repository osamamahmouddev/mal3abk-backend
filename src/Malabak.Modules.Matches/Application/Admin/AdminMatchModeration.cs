using System.Globalization;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Admin;

/// <summary>
/// Ranking correction (TASK-705): an admin fixes a confirmed result proven wrong, rankings are replayed from all confirmed
/// matches, both captains are told and the change is audited (and kept in the match's result audit trail).
/// </summary>
public sealed class AdminMatchModerationService
{
    public const int MaxReasonLength = 1000;

    private readonly IMatchesDbContext _dbContext;
    private readonly RankingRecalculationJob _recalculation;
    private readonly ITeamCaptaincyChecker _teams;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AdminMatchModerationService(
        IMatchesDbContext dbContext,
        RankingRecalculationJob recalculation,
        ITeamCaptaincyChecker teams,
        INotificationPublisher notifications,
        IAuditLog audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _recalculation = recalculation;
        _teams = teams;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<MatchDto>> CorrectResultAsync(Guid matchId, int homeScore, int awayScore, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<MatchDto>(MatchErrors.ResolutionNotesRequired);
        }

        var match = await _dbContext.Matches.FirstOrDefaultAsync(m => m.Id == matchId, cancellationToken);
        if (match is null)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotFound);
        }

        var before = new { match.HomeScore, match.AwayScore };
        var corrected = match.CorrectResult(homeScore, awayScore, adminId, reason, _clock.UtcNow);
        if (corrected.IsFailure)
        {
            return Result.Failure<MatchDto>(corrected.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var replayed = await _recalculation.ExecuteRecalculationAsync(cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.match.result_corrected", nameof(Match), matchId.ToString("D"), adminId, AuditActorType.Admin,
            Before: before, After: new { match.HomeScore, match.AwayScore, MatchesReplayed = replayed }, Reason: reason.Trim()), cancellationToken);

        var score = string.Create(CultureInfo.InvariantCulture, $"{homeScore}-{awayScore}");
        foreach (var teamId in new[] { match.HomeTeamId, match.AwayTeamId })
        {
            if (await _teams.GetCaptainPlayerIdAsync(teamId, cancellationToken) is { } captain && captain != Guid.Empty)
            {
                await _notifications.PublishAsync(new NotificationRequest(captain, NotificationTypes.MatchResultCorrected,
                    new Dictionary<string, string> { ["matchId"] = matchId.ToString("D"), ["score"] = score, ["reason"] = reason.Trim() },
                    $"{NotificationTypes.MatchResultCorrected}:{matchId:N}:{captain:N}:{score}"), cancellationToken);
            }
        }

        var home = await _teams.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home";
        var away = await _teams.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away";
        return Result.Success(MatchMapper.ToDto(match, home, away));
    }
}
