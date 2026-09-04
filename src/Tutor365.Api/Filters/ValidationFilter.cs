using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tutor365.Api.Filters;

/// <summary>Runs FluentValidation validators for every action argument that has one; throws so the middleware formats the error.</summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;
    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg == null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
            if (_services.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(arg), context.HttpContext.RequestAborted);
                if (!result.IsValid) throw new ValidationException(result.Errors);
            }
            else if (arg is System.Collections.IEnumerable list && arg is not string)
            {
                foreach (var item in list)
                {
                    if (item == null) continue;
                    var t = typeof(IValidator<>).MakeGenericType(item.GetType());
                    if (_services.GetService(t) is IValidator v)
                    {
                        var r = await v.ValidateAsync(new ValidationContext<object>(item), context.HttpContext.RequestAborted);
                        if (!r.IsValid) throw new ValidationException(r.Errors);
                    }
                }
            }
        }
        await next();
    }
}
