using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;

public sealed class AcceptTeamInvitationCommandValidator : AbstractValidator<AcceptTeamInvitationCommand>
{
    public AcceptTeamInvitationCommandValidator()
    {
        RuleFor(x => x.PlayerId)
            .NotEmpty()
            .WithMessage("PlayerId is required.");

        RuleFor(x => x.InviteCode)
            .NotEmpty()
            .WithMessage("Invite code is required.")
            .Length(6)
            .WithMessage("Invite code must be exactly 6 characters.");
    }
}
