using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;

public sealed record TransferCaptaincyCommand(
    Guid TeamId,
    Guid NewCaptainPlayerId,
    Guid CurrentCaptainPlayerId) : ICommand<Result>;
