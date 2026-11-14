using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.CreateTeam;

public sealed record CreateTeamCommand(
    Guid CaptainId,
    string Name,
    string? Description,
    string City,
    string Governorate,
    string? District = null,
    string? LogoUrl = null,
    string? CoverUrl = null,
    bool? IsPublic = null,
    bool? IsRecruiting = null,
    bool? IsOpenForChallenges = null) : ICommand<Result<TeamDetailDto>>;
