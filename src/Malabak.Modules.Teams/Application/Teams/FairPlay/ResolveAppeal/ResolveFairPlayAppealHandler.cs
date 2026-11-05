using FluentValidation;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.FairPlay.ResolveAppeal;

public sealed record ResolveFairPlayAppealCommand(
    Guid ReportId,
    Guid AdminUserId,
    bool UpholdViolation,
    string AdminNotes) : ICommand<Result<FairPlayReportDto>>;

public sealed class ResolveFairPlayAppealCommandValidator : AbstractValidator<ResolveFairPlayAppealCommand>
{
    public ResolveFairPlayAppealCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.AdminNotes).NotEmpty().MaximumLength(1000);
    }
}

public sealed class ResolveFairPlayAppealHandler : ICommandHandler<ResolveFairPlayAppealCommand, Result<FairPlayReportDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly ILeaderboardCache _cache;
    private readonly IClock _clock;

    public ResolveFairPlayAppealHandler(
        ITeamsDbContext dbContext,
        ILeaderboardCache cache,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Result<FairPlayReportDto>> HandleAsync(ResolveFairPlayAppealCommand command, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.FairPlayReports
            .FirstOrDefaultAsync(r => r.Id == command.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<FairPlayReportDto>(Error.NotFound("FairPlay.ReportNotFound", "Fair-play report was not found."));
        }

        var resolveResult = report.ResolveAppeal(command.UpholdViolation, command.AdminNotes, command.AdminUserId, _clock.UtcNow);
        if (!resolveResult.IsSuccess)
        {
            return Result.Failure<FairPlayReportDto>(resolveResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(cancellationToken);

        var reportedTeam = await _dbContext.Teams
            .FirstOrDefaultAsync(t => t.Id == report.ReportedTeamId, cancellationToken);

        var dto = new FairPlayReportDto(
            report.Id,
            report.ReporterTeamId,
            report.ReporterPlayerId,
            report.ReportedTeamId,
            reportedTeam?.Name ?? "Reported Team",
            report.MatchId,
            report.Category,
            report.Description,
            report.EvidenceUrls,
            report.Status,
            report.PenaltyType,
            report.PointsDeducted,
            report.AdminDecisionNotes,
            report.AdminReviewedByUserId,
            report.AdminReviewedAtUtc,
            report.AppealReason,
            report.AppealedByPlayerId,
            report.AppealedAtUtc,
            report.AdminAppealNotes,
            report.AdminAppealResolvedByUserId,
            report.AdminAppealResolvedAtUtc,
            report.CreatedAtUtc);

        return Result.Success(dto);
    }
}
