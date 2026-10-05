namespace Nullean.Entities.Extensions.DependencyInjection;

public static class ViolationErrors
{
	/// <summary>ProblemDetails' <c>errors</c> shape: member name (or the constraint's name when the rule is not about one member) to messages. Only errors; warnings and hints do not reject.</summary>
	public static Dictionary<string, string[]> ToDictionary(IEnumerable<Violation> violations) =>
		violations
			.Where(v => v.Severity == Severity.Error)
			.GroupBy(v => v.Member ?? v.ConstraintId ?? "")
			.ToDictionary(g => g.Key, g => g.Select(v => v.Message).ToArray());
}
