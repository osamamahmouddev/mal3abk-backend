using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;

public sealed class RequestToJoinTeamCommandValidator : AbstractValidator<RequestToJoinTeamCommand>
{
    public RequestToJoinTeamCommandValidator()
    {
        RuleFor(x => x.TeamId)
            .NotEmpty();

        RuleFor(x => x.PlayerId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.Message));
    }
}
