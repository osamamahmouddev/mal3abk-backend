using FluentValidation;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.FairPlay.ReviewReport;

public sealed record ReviewFairPlayReportCommand(
    Guid ReportId,
    Guid AdminUserId,
    bool IsApproved,
    FairPlayPenaltyType Penalty,
    int? PointsDeducted,
    string AdminNotes) : ICommand<Result<FairPlayReportDto>>;

public sealed class ReviewFairPlayReportCommandValidator : AbstractValidator<ReviewFairPlayReportCommand>
{
    public ReviewFairPlayReportCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.AdminNotes).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Penalty).IsInEnum();
        When(x => x.IsApproved && x.Penalty == FairPlayPenaltyType.PointsDeduction, () =>
        {
            RuleFor(x => x.PointsDeducted).NotNull().GreaterThan(0).LessThanOrEqualTo(30);
        });
    }
}

public sealed class ReviewFairPlayReportHandler : ICommandHandler<ReviewFairPlayReportCommand, Result<FairPlayReportDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly ILeaderboardCache _cache;
    private readonly IClock _clock;

    public ReviewFairPlayReportHandler(
        ITeamsDbContext dbContext,
        ILeaderboardCache cache,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Result<FairPlayReportDto>> HandleAsync(ReviewFairPlayReportCommand command, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.FairPlayReports
            .FirstOrDefaultAsync(r => r.Id == command.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<FairPlayReportDto>(Error.NotFound("FairPlay.ReportNotFound", "Fair-play report was not found."));
        }

        var reviewResult = report.ReviewReport(
            command.IsApproved,
            command.Penalty,
            command.PointsDeducted,
            command.AdminNotes,
            command.AdminUserId,
            _clock.UtcNow);

        if (!reviewResult.IsSuccess)
        {
            return Result.Failure<FairPlayReportDto>(reviewResult.Error);
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
