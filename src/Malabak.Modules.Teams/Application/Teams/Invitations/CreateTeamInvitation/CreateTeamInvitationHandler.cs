using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;

public sealed class CreateTeamInvitationHandler : ICommandHandler<CreateTeamInvitationCommand, Result<TeamInvitationDto>>
{
    private const int _maxAttempts = 3;

    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ITeamInviteCodeGenerator _inviteCodeGenerator;
    private readonly INotificationPublisher? _notificationPublisher;

    public CreateTeamInvitationHandler(
        ITeamsDbContext dbContext,
        IClock clock,
        ITeamInviteCodeGenerator inviteCodeGenerator,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _inviteCodeGenerator = inviteCodeGenerator;
        _notificationPublisher = notificationPublisher;
    }

    public async Task<Result<TeamInvitationDto>> HandleAsync(CreateTeamInvitationCommand command, CancellationToken cancellationToken = default)
    {
        Team? team = null;
        for (var attempt = 1; attempt <= _maxAttempts && team is null; attempt++)
        {
            try
            {
                var rotated = await TryRotateAsync(command, cancellationToken);
                if (!rotated.IsSuccess)
                {
                    return Result.Failure<TeamInvitationDto>(rotated.Error);
                }

                team = rotated.Value;
            }
            catch (DbUpdateException ex) when (TeamConcurrency.IsCodeOrSlugCollision(ex))
            {
                // Another team took the same code between the check and the save: try a fresh one.
                _dbContext.ChangeTracker.Clear();
            }
        }

        if (team is null)
        {
            return Result.Failure<TeamInvitationDto>(TeamErrors.InviteCodeUnavailable);
        }

        if (_notificationPublisher is not null && command.InvitedPlayerId.HasValue && command.InvitedPlayerId.Value != Guid.Empty)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    command.InvitedPlayerId.Value,
                    NotificationTypes.TeamInvitationReceived,
                    new Dictionary<string, string>
                    {
                        ["teamName"] = team.Name,
                        ["teamId"] = team.Id.ToString("D"),
                        ["inviteCode"] = team.InviteCode
                    },
                    $"team-invitation:{team.Id}:{command.InvitedPlayerId.Value}"),
                cancellationToken);
        }

        return Result.Success(new TeamInvitationDto(team.Id, team.InviteCode, team.InviteCodeExpiresAtUtc));
    }

    private async Task<Result<Team>> TryRotateAsync(CreateTeamInvitationCommand command, CancellationToken cancellationToken)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<Team>(TeamErrors.NotFound);
        }

        // Disbanded teams keep their code in the unique index, so they are checked too.
        var newCode = _inviteCodeGenerator.GenerateCode();
        while (await _dbContext.Teams.IgnoreQueryFilters().AnyAsync(t => t.InviteCode == newCode && t.Id != team.Id, cancellationToken))
        {
            newCode = _inviteCodeGenerator.GenerateCode();
        }

        var nowUtc = _clock.UtcNow;
        DateTime? expiresAt = command.ValidityDays.HasValue
            ? nowUtc.AddDays(command.ValidityDays.Value)
            : null;

        var rotateResult = team.RotateInviteCode(newCode, command.RequestingPlayerId, nowUtc, expiresAt);
        if (!rotateResult.IsSuccess)
        {
            return Result.Failure<Team>(rotateResult.Error);
        }

        // A leader inviting a removed player by name lets them use the code again.
        if (command.InvitedPlayerId is { } invited && invited != Guid.Empty)
        {
            await TeamRejoin.AllowAsync(_dbContext, team.Id, invited, nowUtc, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(team);
    }
}
