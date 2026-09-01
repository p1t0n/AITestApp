using FluentValidation;

namespace ExpertToJob.Application.Experts;

public class SaveExpertValidator : AbstractValidator<SaveExpertDto>
{
    public SaveExpertValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).MaximumLength(200);
        // Email is optional at save time so agent-staged drafts can be honest about a resume that
        // carries no address; promotion to Active is the gate that demands one (P1T-92).
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Email).MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Location).MaximumLength(200);
    }
}
