// Nullean.Entities.CsCheck — the property-test run itself as an event source.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using Nullean.Entities.Reactive;

namespace Nullean.Entities.CsCheck;

public abstract record EntityEvent
{
	public sealed record IterationStarted(long Iteration, string Seed) : EntityEvent;

	public sealed record ConstraintResolved(long Iteration, string Constraint, Verdict Verdict) : EntityEvent;

	public sealed record SmallerFailure(string Seed, IReadOnlyList<string> Trace, IReadOnlyList<string> Violations) : EntityEvent;

	public sealed record Completed(long Iterations, bool Passed) : EntityEvent;
}
