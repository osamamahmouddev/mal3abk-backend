using FluentValidation;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.FairPlay.SubmitReport;

public sealed record SubmitFairPlayReportCommand(
    Guid ReporterTeamId,
    Guid ReporterPlayerId,
    Guid ReportedTeamId,
    FairPlayReportCategory Category,
    string Description,
    Guid? MatchId = null,
    string? EvidenceUrls = null) : ICommand<Result<FairPlayReportDto>>;

public sealed class SubmitFairPlayReportCommandValidator : AbstractValidator<SubmitFairPlayReportCommand>
{
    public SubmitFairPlayReportCommandValidator()
    {
        RuleFor(x => x.ReporterTeamId).NotEmpty();
        RuleFor(x => x.ReporterPlayerId).NotEmpty();
        RuleFor(x => x.ReportedTeamId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.EvidenceUrls).MaximumLength(2000).When(x => !string.IsNullOrEmpty(x.EvidenceUrls));
    }
}

public sealed class SubmitFairPlayReportHandler : ICommandHandler<SubmitFairPlayReportCommand, Result<FairPlayReportDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public SubmitFairPlayReportHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<FairPlayReportDto>> HandleAsync(SubmitFairPlayReportCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Verify caller is active captain or vice-captain of reporting team
        var reporterTeam = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.ReporterTeamId && !t.IsDeleted, cancellationToken);

        if (reporterTeam is null)
        {
            return Result.Failure<FairPlayReportDto>(TeamErrors.NotFound);
        }

        var caller = reporterTeam.Members.FirstOrDefault(m => m.PlayerId == command.ReporterPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure<FairPlayReportDto>(TeamErrors.NotCaptainOrViceCaptain);
        }

        // 2. Verify reported team exists
        var reportedTeam = await _dbContext.Teams
            .FirstOrDefaultAsync(t => t.Id == command.ReportedTeamId && !t.IsDeleted, cancellationToken);

        if (reportedTeam is null)
        {
            return Result.Failure<FairPlayReportDto>(Error.NotFound("Team.ReportedTeamNotFound", "Reported team not found."));
        }

        // 3. Create report (enforces that raw complaints never penalize directly)
        var reportResult = TeamFairPlayReport.Create(
            command.ReporterTeamId,
            command.ReporterPlayerId,
            command.ReportedTeamId,
            command.Category,
            command.Description,
            _clock.UtcNow,
            command.MatchId,
            command.EvidenceUrls);

        if (!reportResult.IsSuccess)
        {
            return Result.Failure<FairPlayReportDto>(reportResult.Error);
        }

        var report = reportResult.Value;
        _dbContext.FairPlayReports.Add(report);
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
