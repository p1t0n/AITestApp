using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EmployeeManager.Web.Infrastructure;

/// <summary>
/// Runs any registered FluentValidation validator against each action argument.
/// A failure throws <see cref="ValidationException"/>, handled by the global exception handler.
/// </summary>
public class ValidationActionFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;
    public ValidationActionFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument));
                if (!result.IsValid)
                    throw new ValidationException(result.Errors);
            }
        }

        await next();
    }
}
