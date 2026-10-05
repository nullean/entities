using Microsoft.AspNetCore.Http;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore;

public static class ConstraintResults
{
	/// <summary>200 with the value, or a 400 ValidationProblem with the violations.</summary>
	public static IResult ToHttpResult<T>(this Result<T> result) => result switch
	{
		Result<T>.Ok ok => TypedResults.Ok(ok.Value),
		Result<T>.Fail fail => TypedResults.ValidationProblem(ViolationErrors.ToDictionary(fail.Violations)),
		_ => throw new InvalidOperationException(),
	};

	/// <summary>Applied and Noop answer 200 with the value; Failure answers 400. The warnings and hints an applied change raised ride along in the 200 body's <c>notices</c>.</summary>
	public static IResult ToHttpResult<T>(this ApplyOutcome<T> outcome) => outcome switch
	{
		ApplyOutcome<T>.Applied { Notices.Count: > 0 } applied => TypedResults.Ok(new { value = applied.Value, notices = applied.Notices.Select(Notice) }),
		ApplyOutcome<T>.Applied applied => TypedResults.Ok(applied.Value),
		ApplyOutcome<T>.Noop noop => TypedResults.Ok(noop.Value),
		ApplyOutcome<T>.Failure failure => TypedResults.ValidationProblem(ViolationErrors.ToDictionary(failure.Violations)),
		_ => throw new InvalidOperationException(),
	};

	private static object Notice(Violation v) =>
		new { member = v.Member, constraintId = v.ConstraintId, severity = v.Severity.ToString(), message = v.Message };
}
