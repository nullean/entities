using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore;

/// <summary>Answers an unhandled <see cref="ConstraintViolationException"/> with a 400 ValidationProblem, so a handler can call <c>ApplyOrThrow</c> or <c>Unwrap</c> with no try/catch.</summary>
public sealed class ConstraintViolationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		if (exception is not ConstraintViolationException violation)
		{
			return false;
		}
		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
		return await problemDetails.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = httpContext,
			Exception = exception,
			ProblemDetails = new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(ViolationErrors.ToDictionary(violation.Violations)),
		}).ConfigureAwait(false);
	}
}

public static class ConstraintProblemDetailsExtensions
{
	/// <summary>Registers problem details and the handler; also call <c>app.UseExceptionHandler()</c>.</summary>
	public static IServiceCollection AddConstraintProblemDetails(this IServiceCollection services)
	{
		services.AddProblemDetails();
		services.AddExceptionHandler<ConstraintViolationExceptionHandler>();
		return services;
	}
}
