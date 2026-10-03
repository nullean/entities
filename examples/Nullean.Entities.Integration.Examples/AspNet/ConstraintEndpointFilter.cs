// ASP.NET Core minimal APIs: validate a request body against a ConstraintSet, answer 400 with a ValidationProblem keyed by member.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Nullean.Entities.Integration.Examples.AspNet;

public static class ViolationErrors
{
	/// <summary>ProblemDetails' <c>errors</c> shape: member name (or the constraint's name when the rule is not about one member) to messages.</summary>
	public static Dictionary<string, string[]> ToDictionary(IEnumerable<Violation> violations) =>
		violations
			.Where(v => v.Severity == Severity.Error)
			.GroupBy(v => v.Member ?? v.ConstraintId ?? "")
			.ToDictionary(g => g.Key, g => g.Select(v => v.Message).ToArray());
}

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

public static class ConstraintEndpointExtensions
{
	/// <summary><c>app.MapPost("/invoices", ...).ValidateWith(InvoiceConstraints.All)</c></summary>
	public static TBuilder ValidateWith<TBuilder, T>(this TBuilder builder, ConstraintSet<T> constraints) where TBuilder : IEndpointConventionBuilder =>
		builder.AddEndpointFilter(new ConstraintFilter<T>(constraints));

	/// <summary>Turns the exception <c>ApplyOrThrow</c> and <c>Unwrap</c> throw into the same 400 response, for handlers that apply a change and let it throw.</summary>
	public static IResult ToProblem(this ConstraintViolationException exception) =>
		Results.ValidationProblem(ViolationErrors.ToDictionary(exception.Violations));
}
