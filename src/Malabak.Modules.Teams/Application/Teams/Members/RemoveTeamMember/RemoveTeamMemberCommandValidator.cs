using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;

public sealed class RemoveTeamMemberCommandValidator : AbstractValidator<RemoveTeamMemberCommand>
{
    public RemoveTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId)
            .NotEmpty()
            .WithMessage("TeamId is required.");

        RuleFor(x => x.MemberPlayerId)
            .NotEmpty()
            .WithMessage("MemberPlayerId is required.");

        RuleFor(x => x.RequestingPlayerId)
            .NotEmpty()
            .WithMessage("RequestingPlayerId is required.");
    }
}
