using FluentValidation;

namespace EmployeeManager.Application.Employees;

public class SaveEmployeeValidator : AbstractValidator<SaveEmployeeDto>
{
    public SaveEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Location).MaximumLength(200);
    }
}
