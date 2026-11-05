using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;

public sealed class TransferCaptaincyCommandValidator : AbstractValidator<TransferCaptaincyCommand>
{
    public TransferCaptaincyCommandValidator()
    {
        RuleFor(x => x.TeamId)
            .NotEmpty()
            .WithMessage("TeamId is required.");

        RuleFor(x => x.NewCaptainPlayerId)
            .NotEmpty()
            .WithMessage("NewCaptainPlayerId is required.");

        RuleFor(x => x.CurrentCaptainPlayerId)
            .NotEmpty()
            .WithMessage("CurrentCaptainPlayerId is required.");

        RuleFor(x => x)
            .Must(x => x.NewCaptainPlayerId != x.CurrentCaptainPlayerId)
            .WithMessage("Cannot transfer captaincy to yourself.");
    }
}
