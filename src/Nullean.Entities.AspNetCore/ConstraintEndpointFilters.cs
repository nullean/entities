using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore;

/// <summary>An endpoint filter that rejects a request whose <typeparamref name="T"/> argument breaks the constraints with a 400 ValidationProblem, before the handler runs.</summary>
public sealed class ConstraintFilter<T>(ConstraintSet<T> constraints) : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var body = context.Arguments.OfType<T>().FirstOrDefault();
		if (body is not null && ViolationErrors.ToDictionary(constraints.Validate(body)) is { Count: > 0 } errors)
		{
			return Results.ValidationProblem(errors);
		}
		return await next(context).ConfigureAwait(false);
	}
}

/// <summary>Validates every handler argument whose type was registered with <c>AddConstraints</c>, so one call on a route group covers all its endpoints.</summary>
public sealed class RegisteredConstraintFilter : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var registry = context.HttpContext.RequestServices.GetService<IConstraintRegistry>();
		if (registry is not null)
		{
			Dictionary<string, string[]>? errors = null;
			foreach (var argument in context.Arguments)
			{
				if (argument is not null && registry.TryValidate(argument, out var violations) && ViolationErrors.ToDictionary(violations) is
					{ Count: > 0 } found)
				{
					errors ??= [];
					foreach (var (key, messages) in found)
					{
						errors[key] = errors.TryGetValue(key, out var existing) ? [.. existing, .. messages] : messages;
					}
				}
			}
			if (errors is not null)
			{
				return Results.ValidationProblem(errors);
			}
		}
		return await next(context).ConfigureAwait(false);
	}
}

public static class ConstraintEndpointExtensions
{
	/// <summary><c>app.MapPost("/invoices", ...).ValidateWith(InvoiceConstraints.All)</c></summary>
	public static TBuilder ValidateWith<TBuilder, T>(this TBuilder builder, ConstraintSet<T> constraints) where TBuilder : IEndpointConventionBuilder =>
		builder.AddEndpointFilter(new ConstraintFilter<T>(constraints));

	/// <summary>For a type that declares its own constraints: <c>app.MapPost("/invoices", ...).ValidateWith&lt;Invoice&gt;()</c></summary>
	public static TBuilder ValidateWith<TBuilder, T>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder where T : IHaveConstraints<T> =>
		builder.ValidateWith(T.Constraints);

	/// <summary>Validates every argument whose type was registered with <c>services.AddConstraints&lt;T&gt;()</c>; put it on a <c>MapGroup</c> to cover the group.</summary>
	public static TBuilder ValidateConstraints<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
		builder.AddEndpointFilter(new RegisteredConstraintFilter());

	/// <summary>Turns the exception <c>ApplyOrThrow</c> and <c>Unwrap</c> throw into the same 400 response, for handlers that apply a change and let it throw.</summary>
	public static IResult ToProblem(this ConstraintViolationException exception) =>
		Results.ValidationProblem(ViolationErrors.ToDictionary(exception.Violations));
}
