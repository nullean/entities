using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore;

/// <summary>Validates action arguments whose type was registered with <c>AddConstraints</c> into <c>ModelState</c>, so <c>[ApiController]</c>'s automatic 400 answers them like any other model error.</summary>
public sealed class ConstraintActionFilter(IConstraintRegistry registry) : IAsyncActionFilter
{
	public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		foreach (var (name, argument) in context.ActionArguments)
		{
			if (argument is not null && registry.TryValidate(argument, out var violations))
			{
				context.ModelState.AddViolations(violations, name);
			}
		}
		if (!context.ModelState.IsValid && context.Controller is ControllerBase controller && controller
				.GetType()
				.GetCustomAttributes(typeof(ApiControllerAttribute), true)
				.Length > 0)
		{
			context.Result = controller.ValidationProblem(context.ModelState);
			return Task.CompletedTask;
		}
		return next();
	}
}

public static class ConstraintMvcExtensions
{
	/// <summary>Adds an error per violation, keyed <c>prefix.Member</c>.</summary>
	public static void AddViolations(this ModelStateDictionary modelState, IEnumerable<Violation> violations, string? prefix = null)
	{
		foreach (var v in violations.Where(v => v.Severity == Severity.Error))
		{
			var key = v.Member ?? v.ConstraintId ?? "";
			modelState.AddModelError(string.IsNullOrEmpty(prefix) ? key : string.IsNullOrEmpty(key) ? prefix : $"{prefix}.{key}", v.Message);
		}
	}

	public static IMvcBuilder AddConstraintValidation(this IMvcBuilder mvc)
	{
		mvc.Services.Configure<MvcOptions>(o => o.Filters.AddService<ConstraintActionFilter>());
		mvc.Services.AddScoped<ConstraintActionFilter>();
		return mvc;
	}
}
