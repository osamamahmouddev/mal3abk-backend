using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Settings.UpdateTeamSettings;

public sealed class UpdateTeamSettingsHandler : ICommandHandler<UpdateTeamSettingsCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateTeamSettingsHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(UpdateTeamSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var result = team.UpdateSettings(
            command.IsPublic,
            command.IsRecruiting,
            command.IsOpenForChallenges,
            command.CallerPlayerId,
            _clock.UtcNow);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
