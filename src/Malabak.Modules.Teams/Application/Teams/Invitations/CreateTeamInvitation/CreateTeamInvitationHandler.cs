using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;

public sealed class CreateTeamInvitationHandler : ICommandHandler<CreateTeamInvitationCommand, Result<TeamInvitationDto>>
{
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
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamInvitationDto>(TeamErrors.NotFound);
        }

        // Generate unique 6-character code
        var newCode = _inviteCodeGenerator.GenerateCode();
        while (await _dbContext.Teams.AnyAsync(t => t.InviteCode == newCode && t.Id != team.Id, cancellationToken))
        {
            newCode = _inviteCodeGenerator.GenerateCode();
        }

        DateTime? expiresAt = command.ValidityDays.HasValue
            ? _clock.UtcNow.AddDays(command.ValidityDays.Value)
            : null;

        var rotateResult = team.RotateInviteCode(newCode, command.RequestingPlayerId, _clock.UtcNow, expiresAt);
        if (!rotateResult.IsSuccess)
        {
            return Result.Failure<TeamInvitationDto>(rotateResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

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
}
