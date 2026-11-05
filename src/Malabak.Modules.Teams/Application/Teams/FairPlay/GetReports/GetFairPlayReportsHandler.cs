using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.FairPlay.GetReports;

public sealed record GetFairPlayReportsQuery(
    FairPlayReportStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<PagedList<FairPlayReportDto>>>;

public sealed class GetFairPlayReportsHandler : IQueryHandler<GetFairPlayReportsQuery, Result<PagedList<FairPlayReportDto>>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetFairPlayReportsHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedList<FairPlayReportDto>>> HandleAsync(GetFairPlayReportsQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 50);

        var queryable = _dbContext.FairPlayReports.AsQueryable();

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(r => r.Status == query.Status.Value);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        var reports = await queryable
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var reportedTeamIds = reports.Select(r => r.ReportedTeamId).Distinct().ToList();
        var teamNames = await _dbContext.Teams
            .Where(t => reportedTeamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var dtos = reports.Select(r => new FairPlayReportDto(
            r.Id,
            r.ReporterTeamId,
            r.ReporterPlayerId,
            r.ReportedTeamId,
            teamNames.GetValueOrDefault(r.ReportedTeamId, "Unknown Team"),
            r.MatchId,
            r.Category,
            r.Description,
            r.EvidenceUrls,
            r.Status,
            r.PenaltyType,
            r.PointsDeducted,
            r.AdminDecisionNotes,
            r.AdminReviewedByUserId,
            r.AdminReviewedAtUtc,
            r.AppealReason,
            r.AppealedByPlayerId,
            r.AppealedAtUtc,
            r.AdminAppealNotes,
            r.AdminAppealResolvedByUserId,
            r.AdminAppealResolvedAtUtc,
            r.CreatedAtUtc)).ToList();

        return Result.Success(new PagedList<FairPlayReportDto>(dtos, totalCount, page, pageSize));
    }
}
