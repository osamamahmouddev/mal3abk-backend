using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Admin;

public sealed record AdminTeamListItem(
    Guid Id, string Name, string City, Guid CaptainId, int Members, double Rating, int MatchesPlayed, bool IsSuspended, string? SuspensionReason,
    int OpenReportsAgainst, DateTime CreatedAtUtc);

public sealed record AdminTeamPage(IReadOnlyList<AdminTeamListItem> Items, int Page, int PageSize, int Total);

/// <summary>
/// Team moderation for platform staff (TASK-705): find teams (with the fair-play reports against them) and suspend or
/// reactivate them. Suspension blocks captain actions across modules, new members and challenges; it is audited and the
/// captain is told.
/// </summary>
public sealed class AdminTeamModerationService
{
    public const int MaxPageSize = 100;

    private readonly ITeamsDbContext _dbContext;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AdminTeamModerationService(ITeamsDbContext dbContext, INotificationPublisher notifications, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AdminTeamPage>> SearchAsync(string? query, bool? suspended, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize || query?.Length > 100)
        {
            return Result.Failure<AdminTeamPage>(Error.Validation("AdminTeams.InvalidFilter", "Invalid team filter."));
        }

        var teams = _dbContext.Teams.AsNoTracking();
        if (suspended is { } s)
        {
            teams = s ? teams.Where(t => t.SuspendedAtUtc != null) : teams.Where(t => t.SuspendedAtUtc == null);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            teams = Guid.TryParse(query.Trim(), out var id)
                ? teams.Where(t => t.Id == id)
                : teams.Where(t => t.Name.ToLower().Contains(term) || t.City.ToLower().Contains(term));
        }

        var total = await teams.CountAsync(cancellationToken);
        var rows = await teams.OrderByDescending(t => t.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.City,
                t.CaptainId,
                t.Rating,
                t.MatchesPlayed,
                t.SuspendedAtUtc,
                t.SuspensionReason,
                t.CreatedAtUtc,
                Members = _dbContext.TeamMembers.Count(m => m.TeamId == t.Id && m.IsActive),
                Reports = _dbContext.FairPlayReports.Count(r => r.ReportedTeamId == t.Id && (r.Status == FairPlayReportStatus.PendingReview || r.Status == FairPlayReportStatus.Appealed))
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(t => new AdminTeamListItem(t.Id, t.Name, t.City, t.CaptainId, t.Members, t.Rating, t.MatchesPlayed,
            t.SuspendedAtUtc is not null, t.SuspensionReason, t.Reports, t.CreatedAtUtc)).ToList();
        return Result.Success(new AdminTeamPage(items, page, pageSize, total));
    }

    public Task<Result<AdminTeamListItem>> SuspendAsync(Guid teamId, string? reason, Guid adminId, CancellationToken cancellationToken = default) =>
        ChangeAsync(teamId, reason, adminId, (t, r) => t.Suspend(r ?? string.Empty, _clock.UtcNow), "admin.team.suspended", NotificationTypes.TeamSuspended, cancellationToken);

    public Task<Result<AdminTeamListItem>> ReactivateAsync(Guid teamId, string? reason, Guid adminId, CancellationToken cancellationToken = default) =>
        ChangeAsync(teamId, reason, adminId, (t, _) => t.Reactivate(_clock.UtcNow), "admin.team.reactivated", NotificationTypes.TeamReactivated, cancellationToken);

    private async Task<Result<AdminTeamListItem>> ChangeAsync(
        Guid teamId, string? reason, Guid adminId, Func<Team, string?, Result> change, string action, string notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > Team.MaxSuspensionReasonLength)
        {
            return Result.Failure<AdminTeamListItem>(Error.Validation("AdminTeams.ReasonRequired", "A reason is required."));
        }

        var team = await _dbContext.Teams.FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return Result.Failure<AdminTeamListItem>(TeamErrors.NotFound);
        }

        var result = change(team, reason.Trim());
        if (result.IsFailure)
        {
            return Result.Failure<AdminTeamListItem>(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry(action, nameof(Team), teamId.ToString("D"), adminId, AuditActorType.Admin,
            After: new { team.IsSuspended }, Reason: reason.Trim()), cancellationToken);
        await _notifications.PublishAsync(new NotificationRequest(team.CaptainId, notification,
            new Dictionary<string, string> { ["teamId"] = team.Id.ToString("D"), ["teamName"] = team.Name, ["reason"] = reason.Trim() },
            $"{notification}:{team.Id:N}:{_clock.UtcNow:yyyyMMddHHmmss}"), cancellationToken);

        var page = await SearchAsync(team.Id.ToString("D"), null, 1, 1, cancellationToken);
        return Result.Success(page.Value.Items[0]);
    }
}
