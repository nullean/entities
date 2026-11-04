// MediatR 14: a pipeline behaviour that validates every request whose type was registered with AddConstraints, before its handler runs.
// MediatR 13 and later is commercially licensed (Lucky Penny Software): production needs a license key (cfg.LicenseKey); development and CI do not.

using MediatR;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.MediatR;

public sealed class ConstraintBehavior<TRequest, TResponse>(IConstraintRegistry registry) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
	public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (registry.TryValidate(request, out var violations) && violations.Any(v => v.Severity == Severity.Error))
		{
			throw new ConstraintViolationException(violations);
		}
		return next(cancellationToken);
	}
}

public sealed partial record RegisterUser(string Email, int Age) : IRequest<string>
{
	[Constraint]
	public static readonly Constraint<RegisterUser> HasEmail = Constraints<RegisterUser>.Check(
		r => r.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]
	);
}

public sealed class RegisterUserHandler : IRequestHandler<RegisterUser, string>
{
	public Task<string> Handle(RegisterUser request, CancellationToken cancellationToken) => Task.FromResult($"registered {request.Email}");
}
