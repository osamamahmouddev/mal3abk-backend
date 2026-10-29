using FluentValidation;
using Malabak.SharedKernel;

namespace Malabak.Api.Endpoints;

public sealed record TestValidationRequest(string Name, string PhoneNumber, decimal Amount);

public sealed class TestValidationRequestValidator : AbstractValidator<TestValidationRequest>
{
    public TestValidationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithErrorCode("validation_failed")
            .WithMessage("Name is required.");

        RuleFor(x => x.PhoneNumber)
            .Must(p => PhoneNumber.Create(p).IsSuccess)
            .WithErrorCode("PhoneNumber.InvalidEgyptianNumber")
            .WithMessage("Phone number must be a valid Egyptian number.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode("Money.NegativeAmount")
            .WithMessage("Amount must be greater than zero.");
    }
}
