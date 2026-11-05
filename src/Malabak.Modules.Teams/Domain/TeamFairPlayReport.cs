using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain;

public sealed class TeamFairPlayReport : Entity<Guid>
{
    public Guid ReporterTeamId { get; private set; }
    public Guid ReporterPlayerId { get; private set; }
    public Guid ReportedTeamId { get; private set; }
    public Guid? MatchId { get; private set; }
    public FairPlayReportCategory Category { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? EvidenceUrls { get; private set; }
    public FairPlayReportStatus Status { get; private set; }
    public FairPlayPenaltyType PenaltyType { get; private set; }
    public int? PointsDeducted { get; private set; }
    public string? AdminDecisionNotes { get; private set; }
    public Guid? AdminReviewedByUserId { get; private set; }
    public DateTime? AdminReviewedAtUtc { get; private set; }
    public string? AppealReason { get; private set; }
    public Guid? AppealedByPlayerId { get; private set; }
    public DateTime? AppealedAtUtc { get; private set; }
    public string? AdminAppealNotes { get; private set; }
    public Guid? AdminAppealResolvedByUserId { get; private set; }
    public DateTime? AdminAppealResolvedAtUtc { get; private set; }

    private TeamFairPlayReport()
    {
    }

    public static Result<TeamFairPlayReport> Create(
        Guid reporterTeamId,
        Guid reporterPlayerId,
        Guid reportedTeamId,
        FairPlayReportCategory category,
        string description,
        DateTime nowUtc,
        Guid? matchId = null,
        string? evidenceUrls = null)
    {
        if (reporterTeamId == reportedTeamId)
        {
            return Result.Failure<TeamFairPlayReport>(
                Error.Validation("FairPlay.CannotReportOwnTeam", "A team cannot file a fair-play report against itself."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<TeamFairPlayReport>(
                Error.Validation("FairPlay.DescriptionRequired", "A clear report description is required."));
        }

        var report = new TeamFairPlayReport
        {
            Id = Guid.NewGuid(),
            ReporterTeamId = reporterTeamId,
            ReporterPlayerId = reporterPlayerId,
            ReportedTeamId = reportedTeamId,
            MatchId = matchId,
            Category = category,
            Description = description.Trim(),
            EvidenceUrls = evidenceUrls?.Trim(),
            Status = FairPlayReportStatus.PendingReview,
            PenaltyType = FairPlayPenaltyType.None,
            PointsDeducted = null,
            CreatedAtUtc = nowUtc
        };

        return Result.Success(report);
    }

    public Result ReviewReport(
        bool isApproved,
        FairPlayPenaltyType penalty,
        int? pointsDeducted,
        string adminNotes,
        Guid adminUserId,
        DateTime nowUtc)
    {
        if (Status != FairPlayReportStatus.PendingReview)
        {
            return Result.Failure(
                Error.Conflict("FairPlay.NotPendingReview", "This report is not awaiting review."));
        }

        if (string.IsNullOrWhiteSpace(adminNotes))
        {
            return Result.Failure(
                Error.Validation("FairPlay.AdminNotesRequired", "Admin notes are required when reviewing a fair-play report."));
        }

        AdminReviewedByUserId = adminUserId;
        AdminReviewedAtUtc = nowUtc;
        AdminDecisionNotes = adminNotes.Trim();

        if (isApproved)
        {
            Status = FairPlayReportStatus.Approved;
            PenaltyType = penalty;
            PointsDeducted = penalty == FairPlayPenaltyType.PointsDeduction ? Math.Max(1, pointsDeducted ?? 1) : null;
        }
        else
        {
            Status = FairPlayReportStatus.Dismissed;
            PenaltyType = FairPlayPenaltyType.None;
            PointsDeducted = null;
        }

        SetUpdatedAt(nowUtc);
        return Result.Success();
    }

    public Result SubmitAppeal(string appealReason, Guid captainPlayerId, DateTime nowUtc)
    {
        if (Status != FairPlayReportStatus.Approved)
        {
            return Result.Failure(
                Error.Conflict("FairPlay.CannotAppeal", "Only approved fair-play penalties can be appealed."));
        }

        if (string.IsNullOrWhiteSpace(appealReason))
        {
            return Result.Failure(
                Error.Validation("FairPlay.AppealReasonRequired", "A detailed reason must be provided for the appeal."));
        }

        Status = FairPlayReportStatus.Appealed;
        AppealReason = appealReason.Trim();
        AppealedByPlayerId = captainPlayerId;
        AppealedAtUtc = nowUtc;
        SetUpdatedAt(nowUtc);

        return Result.Success();
    }

    public Result ResolveAppeal(bool upholdViolation, string adminNotes, Guid adminUserId, DateTime nowUtc)
    {
        if (Status != FairPlayReportStatus.Appealed)
        {
            return Result.Failure(
                Error.Conflict("FairPlay.NotUnderAppeal", "This report is not currently under appeal."));
        }

        if (string.IsNullOrWhiteSpace(adminNotes))
        {
            return Result.Failure(
                Error.Validation("FairPlay.AppealNotesRequired", "Admin notes are required when resolving an appeal."));
        }

        AdminAppealResolvedByUserId = adminUserId;
        AdminAppealResolvedAtUtc = nowUtc;
        AdminAppealNotes = adminNotes.Trim();

        if (upholdViolation)
        {
            Status = FairPlayReportStatus.AppealUpheld;
        }
        else
        {
            Status = FairPlayReportStatus.AppealOverturned;
            PenaltyType = FairPlayPenaltyType.None;
            PointsDeducted = null;
        }

        SetUpdatedAt(nowUtc);
        return Result.Success();
    }
}
