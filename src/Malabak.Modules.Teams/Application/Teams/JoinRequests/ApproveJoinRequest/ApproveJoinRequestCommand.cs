using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;

public sealed record ApproveJoinRequestCommand(
    Guid TeamId,
    Guid RequestId,
    Guid ReviewerPlayerId) : ICommand<Result>;
