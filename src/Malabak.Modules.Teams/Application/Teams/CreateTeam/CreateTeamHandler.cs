using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.CreateTeam;

public sealed class CreateTeamHandler : ICommandHandler<CreateTeamCommand, Result<TeamDetailDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ITeamSlugGenerator _slugGenerator;
    private readonly ITeamInviteCodeGenerator _inviteCodeGenerator;

    public CreateTeamHandler(
        ITeamsDbContext dbContext,
        IClock clock,
        ITeamSlugGenerator slugGenerator,
        ITeamInviteCodeGenerator inviteCodeGenerator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _slugGenerator = slugGenerator;
        _inviteCodeGenerator = inviteCodeGenerator;
    }

    public async Task<Result<TeamDetailDto>> HandleAsync(CreateTeamCommand command, CancellationToken cancellationToken = default)
    {
        // 1. Generate unique slug
        var baseSlug = _slugGenerator.GenerateSlug(command.Name);
        var slug = baseSlug;
        var attempt = 1;

        while (await _dbContext.Teams.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            attempt++;
            slug = $"{baseSlug}-{attempt}";
        }

        // 2. Generate unique 6-character invite code
        var inviteCode = _inviteCodeGenerator.GenerateCode();
        while (await _dbContext.Teams.AnyAsync(t => t.InviteCode == inviteCode, cancellationToken))
        {
            inviteCode = _inviteCodeGenerator.GenerateCode();
        }

        // 3. Create Team Aggregate
        var teamResult = Team.Create(
            Guid.NewGuid(),
            command.Name,
            slug,
            command.City,
            command.Governorate,
            command.CaptainId,
            inviteCode,
            _clock.UtcNow,
            command.Description,
            command.District,
            command.LogoUrl,
            command.CoverUrl);

        if (!teamResult.IsSuccess)
        {
            return Result.Failure<TeamDetailDto>(teamResult.Error);
        }

        var team = teamResult.Value;
        _dbContext.Teams.Add(team);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(TeamMapper.ToDetailDto(team, includePrivateInfo: true));
    }
}
