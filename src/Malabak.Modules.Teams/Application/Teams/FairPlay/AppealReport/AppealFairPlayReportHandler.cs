using FluentValidation;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.FairPlay.AppealReport;

public sealed record AppealFairPlayReportCommand(
    Guid ReportId,
    Guid CaptainPlayerId,
    string AppealReason) : ICommand<Result<FairPlayReportDto>>;

public sealed class AppealFairPlayReportCommandValidator : AbstractValidator<AppealFairPlayReportCommand>
{
    public AppealFairPlayReportCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.CaptainPlayerId).NotEmpty();
        RuleFor(x => x.AppealReason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AppealFairPlayReportHandler : ICommandHandler<AppealFairPlayReportCommand, Result<FairPlayReportDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public AppealFairPlayReportHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<FairPlayReportDto>> HandleAsync(AppealFairPlayReportCommand command, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.FairPlayReports
            .FirstOrDefaultAsync(r => r.Id == command.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<FairPlayReportDto>(Error.NotFound("FairPlay.ReportNotFound", "Fair-play report was not found."));
        }

        // Verify caller is captain of the penalized reported team
        var reportedTeam = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == report.ReportedTeamId && !t.IsDeleted, cancellationToken);

        if (reportedTeam is null)
        {
            return Result.Failure<FairPlayReportDto>(TeamErrors.NotFound);
        }

        var isCaptain = reportedTeam.Members.Any(m => m.PlayerId == command.CaptainPlayerId && m.Role == TeamRole.Captain && m.IsActive);
        if (!isCaptain)
        {
            return Result.Failure<FairPlayReportDto>(TeamErrors.NotCaptain);
        }

        var appealResult = report.SubmitAppeal(command.AppealReason, command.CaptainPlayerId, _clock.UtcNow);
        if (!appealResult.IsSuccess)
        {
            return Result.Failure<FairPlayReportDto>(appealResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new FairPlayReportDto(
            report.Id,
            report.ReporterTeamId,
            report.ReporterPlayerId,
            report.ReportedTeamId,
            reportedTeam.Name,
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
