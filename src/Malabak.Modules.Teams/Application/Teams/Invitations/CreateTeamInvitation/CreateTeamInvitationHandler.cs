using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;

public sealed class CreateTeamInvitationHandler : ICommandHandler<CreateTeamInvitationCommand, Result<TeamInvitationDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ITeamInviteCodeGenerator _inviteCodeGenerator;

    public CreateTeamInvitationHandler(
        ITeamsDbContext dbContext,
        IClock clock,
        ITeamInviteCodeGenerator inviteCodeGenerator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _inviteCodeGenerator = inviteCodeGenerator;
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

        return Result.Success(new TeamInvitationDto(team.Id, team.InviteCode, team.InviteCodeExpiresAtUtc));
    }
}
