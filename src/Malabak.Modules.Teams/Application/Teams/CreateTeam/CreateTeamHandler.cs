using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.CreateTeam;

public sealed class CreateTeamHandler : ICommandHandler<CreateTeamCommand, Result<TeamDetailDto>>
{
    private const int _maxAttempts = 3;

    private readonly ITeamsDbContext _dbContext;
    private readonly IUserAccountsContract? _accounts;
    private readonly IClock _clock;
    private readonly ITeamSlugGenerator _slugGenerator;
    private readonly ITeamInviteCodeGenerator _inviteCodeGenerator;

    public CreateTeamHandler(
        ITeamsDbContext dbContext,
        IClock clock,
        ITeamSlugGenerator slugGenerator,
        ITeamInviteCodeGenerator inviteCodeGenerator,
        IUserAccountsContract? accounts = null)
    {
        _accounts = accounts;
        _dbContext = dbContext;
        _clock = clock;
        _slugGenerator = slugGenerator;
        _inviteCodeGenerator = inviteCodeGenerator;
    }

    public async Task<Result<TeamDetailDto>> HandleAsync(CreateTeamCommand command, CancellationToken cancellationToken = default)
    {
        // The slug and invite code are checked first, but two creations can still pick the same value at once; the
        // unique indexes catch that and the whole attempt is retried with fresh values.
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return await TryCreateAsync(command, cancellationToken);
            }
            catch (DbUpdateException ex) when (TeamConcurrency.IsCodeOrSlugCollision(ex) && attempt < _maxAttempts)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return Result.Failure<TeamDetailDto>(TeamErrors.InviteCodeUnavailable);
    }

    private async Task<Result<TeamDetailDto>> TryCreateAsync(CreateTeamCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await TeamConcurrency.BeginAsync(_dbContext, cancellationToken);
        await TeamConcurrency.LockPlayerAsync(_dbContext, command.CaptainId, cancellationToken);

        // Counted under the player lock, so parallel creations or joins cannot pass the limit together.
        var activeTeamsCount = await _dbContext.TeamMembers
            .CountAsync(m => m.PlayerId == command.CaptainId && m.IsActive, cancellationToken);
        if (activeTeamsCount >= Team.MaxTeamsPerPlayer)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.MaxPlayerTeamsExceeded);
        }

        // Disbanded (soft-deleted) teams keep their slug and code in the unique indexes, so they are checked too.
        var baseSlug = _slugGenerator.GenerateSlug(command.Name);
        var slug = baseSlug;
        var suffix = 1;
        while (await _dbContext.Teams.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }

        var inviteCode = _inviteCodeGenerator.GenerateCode();
        while (await _dbContext.Teams.IgnoreQueryFilters().AnyAsync(t => t.InviteCode == inviteCode, cancellationToken))
        {
            inviteCode = _inviteCodeGenerator.GenerateCode();
        }

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
            command.CoverUrl,
            isPublic: command.IsPublic ?? true,
            isRecruiting: command.IsRecruiting ?? true,
            isOpenForChallenges: command.IsOpenForChallenges ?? true);

        if (!teamResult.IsSuccess)
        {
            return Result.Failure<TeamDetailDto>(teamResult.Error);
        }

        var team = teamResult.Value;
        _dbContext.Teams.Add(team);

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var detail = TeamMapper.ToDetailDto(team, includePrivateInfo: true);
        return Result.Success(detail with { Members = await detail.Members.WithNamesAsync(_accounts, cancellationToken) });
    }
}
