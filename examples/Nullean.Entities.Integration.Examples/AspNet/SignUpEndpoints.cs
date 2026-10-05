// ASP.NET Core minimal APIs with Nullean.Entities.AspNetCore: constraints answer 400 with a ValidationProblem keyed by member.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.AspNetCore;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Integration.Examples.AspNet;

public sealed partial record SignUp(string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<SignUp> HasEmail = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]
	);
}

public static class SignUpEndpoints
{
	public static IServiceCollection AddSignUp(this IServiceCollection services) =>
		services.AddConstraints<SignUp>().AddConstraintProblemDetails();

	public static void MapSignUp(this WebApplication app)
	{
		app.UseExceptionHandler();

		// One endpoint, named explicitly.
		app.MapPost("/signup", (SignUp s) => TypedResults.Ok(s.Email)).ValidateWith<RouteHandlerBuilder, SignUp>();

		// A whole group: every argument whose type was registered with AddConstraints is validated.
		var api = app.MapGroup("/api").ValidateConstraints();
		api.MapPost("/signup", (SignUp s) => TypedResults.Ok(s.Email));
	}
}
