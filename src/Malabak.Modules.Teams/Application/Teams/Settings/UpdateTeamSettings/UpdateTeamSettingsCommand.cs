using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Settings.UpdateTeamSettings;

public sealed record UpdateTeamSettingsCommand(
    Guid TeamId,
    Guid CallerPlayerId,
    bool IsPublic,
    bool IsRecruiting,
    bool IsOpenForChallenges) : ICommand<Result>;
