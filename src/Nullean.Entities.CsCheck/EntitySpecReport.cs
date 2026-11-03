using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// What a spec check (<c>Exhaustive</c>, <c>Sample</c>, <c>Conform</c>) found: CsCheck's report, without its state type. CsCheck 5 made
/// its report generic in the state, and the state the specs explore is internal to this library, so this carries the same numbers
/// and the same text instead.
/// </summary>
public sealed class EntitySpecReport
{
	private readonly string _text;

	private EntitySpecReport(string text) => _text = text;

	/// <summary>Whether the search finished: every reachable state within the limits was explored.</summary>
	public bool Closed { get; private init; }

	/// <summary>The distinct states found.</summary>
	public int States { get; private init; }

	/// <summary>The transitions taken.</summary>
	public long Transitions { get; private init; }

	/// <summary>Transitions into a state already found.</summary>
	public long Revisits { get; private init; }

	/// <summary>The deepest level the search reached.</summary>
	public int Depth { get; private init; }

	/// <summary>States with no way out that are not declared terminal.</summary>
	public int DeadlockStates { get; private init; }

	/// <summary>The walks that ended in such a state.</summary>
	public long DeadlockTraces { get; private init; }

	/// <summary>States declared terminal that were reached.</summary>
	public int TerminalStates { get; private init; }

	/// <summary>A walk into a deadlock, as text, when there is one.</summary>
	public string? DeadlockTrace { get; private init; }

	/// <summary>States cut off by a boundary.</summary>
	public int Pruned { get; private init; }

	/// <summary>For a sampled check: the walks taken.</summary>
	public long TracesWalked { get; private init; }

	/// <summary>For a sampled check: the steps taken over all walks.</summary>
	public long StepsWalked { get; private init; }

	/// <summary>For <c>ConformExhaustive</c>: the states whose transitions were checked against the implementation.</summary>
	public int ConformedStates { get; private init; }

	/// <summary>For <c>ConformExhaustive</c>: the transitions checked against the implementation.</summary>
	public long ConformedTransitions { get; private init; }

	/// <summary>Anything CsCheck wants to add, such as why a search stopped.</summary>
	public string? Note { get; private init; }

	/// <summary>Rules whose guard was never true.</summary>
	public IReadOnlyList<string> NeverTriggered { get; private init; } = [];

	/// <summary>Actions or transitions that never fired.</summary>
	public IReadOnlyList<string> NeverFired { get; private init; } = [];

	/// <summary>CsCheck's own report text.</summary>
	public override string ToString() => _text;

	internal static EntitySpecReport From<S>(SpecReport<S> report) =>
		new(report.ToString())
		{
			Closed = report.Closed,
			States = report.States,
			Transitions = report.Transitions,
			Revisits = report.Revisits,
			Depth = report.Depth,
			DeadlockStates = report.DeadlockStates,
			DeadlockTraces = report.DeadlockTraces,
			TerminalStates = report.TerminalStates,
			DeadlockTrace = report.DeadlockTrace,
			Pruned = report.Pruned,
			TracesWalked = report.TracesWalked,
			StepsWalked = report.StepsWalked,
			ConformedStates = report.ConformedStates,
			ConformedTransitions = report.ConformedTransitions,
			Note = report.Note,
			NeverTriggered = [.. report.NeverTriggered],
			NeverFired = [.. report.NeverFired],
		};
}
