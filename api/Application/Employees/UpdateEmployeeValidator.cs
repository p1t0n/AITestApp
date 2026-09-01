using FluentValidation;

namespace ExpertToJob.Application.Employees;

public class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeDto>
{
    public UpdateEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title is not null);
        // Mirrors SaveEmployeeValidator: email is optional even when supplied, but must be a
        // well-formed address if non-blank (P1T-92).
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Email).MaximumLength(256).When(x => x.Email is not null);
        RuleFor(x => x.Phone).MaximumLength(50).When(x => x.Phone is not null);
        RuleFor(x => x.Location).MaximumLength(200).When(x => x.Location is not null);
    }
}
