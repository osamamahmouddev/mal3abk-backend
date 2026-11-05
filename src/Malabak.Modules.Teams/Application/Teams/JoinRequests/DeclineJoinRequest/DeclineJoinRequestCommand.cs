using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.DeclineJoinRequest;

public sealed record DeclineJoinRequestCommand(
    Guid TeamId,
    Guid RequestId,
    Guid ReviewerPlayerId) : ICommand<Result>;
