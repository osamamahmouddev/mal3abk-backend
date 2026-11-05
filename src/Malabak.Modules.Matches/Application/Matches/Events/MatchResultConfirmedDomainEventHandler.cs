using Malabak.Modules.Matches.Domain.Events;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Matches.Application.Matches.Events;

public sealed class MatchResultConfirmedDomainEventHandler : IDomainEventHandler<MatchResultConfirmedDomainEvent>
{
    private readonly ITeamMatchResultRecorder _teamMatchResultRecorder;
    private readonly ILogger<MatchResultConfirmedDomainEventHandler> _logger;

    public MatchResultConfirmedDomainEventHandler(
        ITeamMatchResultRecorder teamMatchResultRecorder,
        ILogger<MatchResultConfirmedDomainEventHandler> logger)
    {
        _teamMatchResultRecorder = teamMatchResultRecorder;
        _logger = logger;
    }

    public async Task HandleAsync(MatchResultConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "Handling MatchResultConfirmedDomainEvent for match {MatchId}: Home {HomeTeamId} ({HomeScore}) vs Away {AwayTeamId} ({AwayScore}), outcome: {Outcome}",
            domainEvent.MatchId,
            domainEvent.HomeTeamId,
            domainEvent.HomeScore,
            domainEvent.AwayTeamId,
            domainEvent.AwayScore,
            domainEvent.Outcome);

        await _teamMatchResultRecorder.RecordConfirmedResultAsync(
            domainEvent.HomeTeamId,
            domainEvent.AwayTeamId,
            domainEvent.HomeScore,
            domainEvent.AwayScore,
            cancellationToken);
    }
}
