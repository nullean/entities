// Nullean.Entities — StateMachine<TState>: a declared graph of states and named transitions,
// validated eagerly for the four structural properties a hand-rolled Transitions(...).Allow(...)
// list cannot check for itself (every case mentioned, every state reachable, no dead ends, no
// traps). See the "State machines" section of the root README for the full design.
//
// TState's "cases" are types, not values: Initial<T>/State<T>/Terminal<T>/On(...).To<T>() are all
// generic over a case TYPE, not a case VALUE, because a transition's guard (`when:`) needs
// somewhere to carry the source state's own data (e.g. `p => p.Lines.Count > 0`). This is why the
// value-based Constraints<T>.Transitions<TState>(selector).Allow(from, to) builder in
// Constraints.cs stays the right tool for an enum-shaped TState — this type exists for the case
// where each state is its own shape.
//
// A case type is deliberately never required to satisfy `where T : TState`. A C# 11 union's case
// types are not subtypes of the union — see ReactiveConstraints.cs's predicate-based overloads for
// the exact same problem solved the same way elsewhere in this codebase — so every generic method
// below takes a bare, unconstrained <T>. Internally, everything is keyed by `Type` (via typeof(T))
// and matched against real values by `value!.GetType()`, which is exactly the case type whether
// TState is a union, an abstract-record hierarchy, or (trivially) the state type itself — never by
// trying to convert between T and TState, which a union cannot do through the type system either.
// See StateMachineCases below for how a union's own case set is discovered by reflection — it
// turned out not to be op_Implicit, which is why that type's header comment explains it at length.
//
// NESM001-004 are checked eagerly, inside Build() (so at the static field initializer that
// constructs the machine, not by a separate compile-time analyzer yet — see the root README's
// "coming next" section for that gap). Guards are deliberately ignored by all four: they are
// runtime data, and whether one actually lets a transition fire is what AsConstraint/AsReactive
// and the property tester answer, not what the shape of the graph can answer on its own.
//
// No doc comments yet on most members (checkpoint-A exploration code) - CS1591 is suppressed
// project-wide in Directory.Build.props rather than per file.

namespace Nullean.Entities;

// ── StateRole ─────────────────────────────────────────────────────────────────

internal enum StateRole
{
	Initial,
	Normal,
	Terminal,
}

// ── StateMachineEdge<TState> ──────────────────────────────────────────────────

/// <summary>
/// One declared transition. <see cref="Guard"/>, if present, is checked against the source
/// value's own data by <see cref="StateMachine{TState}.Allows"/> — never by the structural
/// checks in <see cref="StateMachine{TState}.Build"/>, which only look at <see cref="From"/>/<see cref="To"/>.
/// </summary>
public sealed record StateMachineEdge<TState>(string Name, Type From, Type To, Func<TState, bool>? Guard = null);

// ── StateMachineDefinitionException ───────────────────────────────────────────

/// <summary>
/// Thrown by <see cref="StateMachine{TState}.Define"/> when the declared graph fails one of the
/// four structural checks. <see cref="Code"/> is one of NESM001 (exhaustiveness), NESM002
/// (reachability), NESM003 (dead end) or NESM004 (trap).
/// </summary>
public sealed class StateMachineDefinitionException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}

// ── StateMachine<TState> ──────────────────────────────────────────────────────

/// <summary>
/// A declared, eagerly-validated graph over <typeparamref name="TState"/>'s cases. Built only
/// through <see cref="Define"/>. Derive a synchronous <see cref="Constraint{T}"/> with
/// <see cref="AsConstraint"/>, a diagram with <see cref="ToMermaid"/>, a reactive constraint over
/// an event stream with Nullean.Entities.Reactive's <c>AsReactive</c> extension, or a graph walk
/// generator with Nullean.Entities.CsCheck's <c>Walks</c> extension.
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class StateMachine<TState>
{
	private readonly Type _initial;
	private readonly IReadOnlyDictionary<Type, StateRole> _states;
	private readonly IReadOnlyList<StateMachineEdge<TState>> _edges;

	internal StateMachine(Type initial, IReadOnlyDictionary<Type, StateRole> states, IReadOnlyList<StateMachineEdge<TState>> edges)
	{
		_initial = initial;
		_states = states;
		_edges = edges;
	}

	/// <summary>Every declared state, as the case type it was declared with.</summary>
	public IReadOnlyCollection<Type> States => (IReadOnlyCollection<Type>)_states.Keys;

	/// <summary>Every declared transition, in declaration order.</summary>
	public IReadOnlyList<StateMachineEdge<TState>> Edges => _edges;

	public Type Initial => _initial;

	public bool IsTerminal(Type state) => _states.TryGetValue(state, out var role) && role == StateRole.Terminal;

	/// <summary>
	/// The inverse of <see cref="Allows"/>'s own unwrapping: builds a <typeparamref name="TState"/>
	/// value out of <paramref name="caseInstance"/>, an instance of the declared case
	/// <paramref name="caseType"/>. For the abstract-hierarchy shape <paramref name="caseInstance"/>
	/// already is a <typeparamref name="TState"/> (an upcast); for a union it calls the
	/// compiler-generated single-parameter constructor for that case — see
	/// <see cref="StateMachineRuntime"/>'s own header comment. Exists for
	/// Nullean.Entities.CsCheck's guard-aware walk generator, which generates a real case instance
	/// (via <c>GenAuto.Of&lt;TCase&gt;()</c>) and needs it back as a real <typeparamref name="TState"/>
	/// to evaluate <see cref="StateMachineEdge{TState}.Guard"/> and <see cref="Allows"/> against.
	/// </summary>
	public TState Wrap(Type caseType, object caseInstance) => StateMachineRuntime.Wrap<TState>(caseType, caseInstance);

	/// <summary>
	/// True when <paramref name="from"/> → <paramref name="to"/> matches a declared edge (by the
	/// values' own runtime types) and that edge's guard, if any, holds for <paramref name="from"/>.
	/// </summary>
	public bool Allows(TState from, TState to)
	{
		var fromType = StateMachineRuntime.CaseTypeOf(from!);
		var toType = StateMachineRuntime.CaseTypeOf(to!);
		foreach (var edge in _edges)
		{
			if (edge.From == fromType && edge.To == toType && (edge.Guard is null || edge.Guard(from)))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Lifts this machine onto <see cref="Constraints{T}.Transitions(Func{T, T, bool}, string)"/>:
	/// violated whenever a (previous, next) pair isn't allowed by <see cref="Allows"/>.
	/// <paramref name="message"/> is optional, same as every other <c>Constraints&lt;T&gt;</c>
	/// builder — omitted, the default names the constraint and the actual pair that broke it.
	/// </summary>
	public Constraint<TState> AsConstraint(string? message = null) => Constraints<TState>.Transitions(Allows, message);

	/// <summary>
	/// A Mermaid <c>stateDiagram-v2</c> rendering of the declared graph — the initial state gets
	/// an incoming <c>[*]</c> edge, every terminal state an outgoing one, every declared
	/// transition an edge labelled with its name.
	/// </summary>
	public string ToMermaid()
	{
		var culture = System.Globalization.CultureInfo.InvariantCulture;
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("stateDiagram-v2");
		sb.AppendLine(culture, $"    [*] --> {_initial.Name}");
		foreach (var edge in _edges)
		{
			sb.AppendLine(culture, $"    {edge.From.Name} --> {edge.To.Name} : {edge.Name}");
		}
		foreach (var (state, role) in _states)
		{
			if (role == StateRole.Terminal)
			{
				sb.AppendLine(culture, $"    {state.Name} --> [*]");
			}
		}
		return sb.ToString();
	}

	/// <summary>A fresh, empty coverage tracker for this machine — see <see cref="StateMachineCoverage{TState}"/>.</summary>
	public StateMachineCoverage<TState> NewCoverageTracker() => new(_edges);

	/// <summary>
	/// Builds and validates the graph a <see cref="Define"/> call assembled. Internal: the only
	/// caller is <see cref="Define"/> itself, immediately after running the caller's builder
	/// lambda.
	/// </summary>
	internal static StateMachine<TState> Build(StateMachineBuilder<TState> builder)
	{
		var states = builder.States;
		var edges = builder.Edges;
		var initial = builder.InitialState;

		if (initial is null || states.Count(s => s.Value == StateRole.Initial) != 1)
		{
			throw new StateMachineDefinitionException(
				"NESM002",
				$"StateMachine<{typeof(TState).Name}> must declare exactly one Initial state before reachability can be checked; found {states.Count(s => s.Value == StateRole.Initial)}."
			);
		}

		CheckExhaustive(states, edges);
		CheckReachable(initial, states, edges);
		CheckNoDeadEnds(states, edges);
		CheckNoTraps(states, edges);

		return new StateMachine<TState>(initial, states, edges);
	}

	// NESM001: every case of TState must have an explicit Initial/State/Terminal declaration — and,
	// since To<TTo>/On(...).To<TTo> take TTo with no constraint tying it to a declared state at
	// all, so must every type an edge actually mentions as its From or To. A case discovered on
	// TState but never declared, and an edge pointing at a type nobody declared, are the same
	// mistake: something the machine references without ever saying what role it plays.
	private static void CheckExhaustive(IReadOnlyDictionary<Type, StateRole> states, IReadOnlyList<StateMachineEdge<TState>> edges)
	{
		var cases = StateMachineCases.Discover(typeof(TState));
		var mentioned = edges.SelectMany(e => new[] { e.From, e.To });
		var missing = cases.Concat(mentioned).Where(c => !states.ContainsKey(c)).Distinct().ToArray();
		if (missing.Length > 0)
		{
			throw new StateMachineDefinitionException(
				"NESM001",
				$"StateMachine<{typeof(TState).Name}> does not mention {string.Join(", ", missing.Select(m => m.Name))} — " +
				"every case needs an Initial<T>, State<T> or Terminal<T> declaration."
			);
		}
	}

	// NESM002: every declared state must be reachable from Initial by following declared edges.
	private static void CheckReachable(Type initial, IReadOnlyDictionary<Type, StateRole> states, IReadOnlyList<StateMachineEdge<TState>> edges)
	{
		var reached = new HashSet<Type> { initial };
		var frontier = new Queue<Type>();
		frontier.Enqueue(initial);
		while (frontier.Count > 0)
		{
			var current = frontier.Dequeue();
			foreach (var edge in edges)
			{
				if (edge.From == current && reached.Add(edge.To))
				{
					frontier.Enqueue(edge.To);
				}
			}
		}

		var unreachable = states.Keys.Where(s => !reached.Contains(s)).ToArray();
		if (unreachable.Length > 0)
		{
			throw new StateMachineDefinitionException(
				"NESM002",
				$"StateMachine<{typeof(TState).Name}> cannot reach {string.Join(", ", unreachable.Select(s => s.Name))} from {initial.Name} — " +
				"no declared edge leads there."
			);
		}
	}

	// NESM003: every non-terminal declared state must have at least one outgoing edge.
	private static void CheckNoDeadEnds(IReadOnlyDictionary<Type, StateRole> states, IReadOnlyList<StateMachineEdge<TState>> edges)
	{
		var withOutgoing = edges.Select(e => e.From).ToHashSet();
		var deadEnds = states.Where(s => s.Value != StateRole.Terminal && !withOutgoing.Contains(s.Key)).Select(s => s.Key).ToArray();
		if (deadEnds.Length > 0)
		{
			throw new StateMachineDefinitionException(
				"NESM003",
				$"StateMachine<{typeof(TState).Name}> has no outgoing transition from {string.Join(", ", deadEnds.Select(s => s.Name))} — " +
				"mark it Terminal<T>() if that is intentional, or add a transition out of it."
			);
		}
	}

	// NESM004: every declared state must be able to reach some Terminal state by following edges.
	private static void CheckNoTraps(IReadOnlyDictionary<Type, StateRole> states, IReadOnlyList<StateMachineEdge<TState>> edges)
	{
		var terminals = states.Where(s => s.Value == StateRole.Terminal).Select(s => s.Key).ToHashSet();
		var canReachTerminal = new HashSet<Type>(terminals);
		var frontier = new Queue<Type>(terminals);
		while (frontier.Count > 0)
		{
			var current = frontier.Dequeue();
			foreach (var edge in edges)
			{
				if (edge.To == current && canReachTerminal.Add(edge.From))
				{
					frontier.Enqueue(edge.From);
				}
			}
		}

		var traps = states.Keys.Where(s => !canReachTerminal.Contains(s)).ToArray();
		if (traps.Length > 0)
		{
			throw new StateMachineDefinitionException(
				"NESM004",
				$"StateMachine<{typeof(TState).Name}> has no path from {string.Join(", ", traps.Select(s => s.Name))} to any Terminal<T>() state."
			);
		}
	}

	/// <summary>
	/// Runs <paramref name="build"/> against a fresh <see cref="StateMachineBuilder{TState}"/> and
	/// validates the result. Throws <see cref="StateMachineDefinitionException"/> if the declared
	/// graph fails exhaustiveness (NESM001), reachability (NESM002), has a dead end (NESM003), or
	/// a state that cannot reach a terminal (NESM004).
	/// </summary>
#pragma warning disable CA1000
	public static StateMachine<TState> Define(Func<StateMachineBuilder<TState>, StateMachineBuilder<TState>> build) =>
		Build(build(new StateMachineBuilder<TState>()));
#pragma warning restore CA1000
}

// ── StateMachineCases ─────────────────────────────────────────────────────────
// Discovers the full set of case types for a state type, for NESM001's exhaustiveness check.
// Two shapes are recognised, tried in this order:
//   1. A C# 11 union. Confirmed by hand against the installed 11.0.100-preview.7 SDK (reflecting
//      over a `readonly union Name(Case1, Case2);` declaration, not guessed): the compiler lowers
//      it to a sealed readonly struct implementing `System.Runtime.CompilerServices.IUnion`,
//      carrying a `[System.Runtime.CompilerServices.UnionAttribute]`, one `object Value` backing
//      field, and — this is the part that matters here — one public single-parameter constructor
//      per case, `.ctor(TCase)`. There is no `op_Implicit`; `OrderStateEventUnion placed = new
//      OrderStateEventCases.Placed(...)` in OrderStateExamplesNet11.cs works because the compiler
//      recognises the attribute and matches the expression's type against a constructor's single
//      parameter, not through the normal user-defined-conversion rules reflection can see. Hence
//      matching by attribute name (not the type, which is internal to the runtime and not meant to
//      be referenced) plus walking single-parameter constructors here, rather than op_Implicit.
//   2. An abstract/sealed hierarchy fallback (the pre-union style every example in this repo also
//      supports): every non-abstract type in TState's own assembly that TState can be assigned
//      from, other than TState itself.
// If neither finds anything, TState itself is the only case — a trivial one-state machine.
//
// Reflection, not syntax, because this runs at the machine's own construction, not at compile
// time — see the type's own header comment for why that is the current, honest scope.

[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
internal static class StateMachineCases
{
	private const string UnionAttributeFullName = "System.Runtime.CompilerServices.UnionAttribute";

	public static Type[] Discover(Type stateType)
	{
		if (IsUnion(stateType))
		{
			return [
				.. stateType
					.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
					.Where(c => c.GetParameters().Length == 1)
					.Select(c => c.GetParameters()[0].ParameterType)
					.Distinct(),
			];
		}

		var hierarchyCases = stateType.Assembly.GetTypes().Where(t => t != stateType && !t.IsAbstract && stateType.IsAssignableFrom(t)).ToArray();
		if (hierarchyCases.Length > 0)
		{
			return hierarchyCases;
		}

		return [stateType];
	}

	internal static bool IsUnion(Type stateType) =>
		stateType.IsValueType && stateType.GetCustomAttributesData().Any(a => a.AttributeType.FullName == UnionAttributeFullName);
}

// ── StateMachineRuntime ────────────────────────────────────────────────────────
// Unwraps an actual TState value to the real case instance boxed inside it. For the abstract-
// hierarchy shape, a TState variable already holds a reference to the case instance directly, so
// GetType() on it is enough. For a union, TState is a compiler-synthesized readonly struct (see
// StateMachineCases's header comment) and the case instance lives in its own `object Value`
// property, not as the struct's own runtime type — `((UnionOrderState)value).GetType()` always
// returns `UnionOrderState` itself, never the case. Every runtime match against a declared edge
// (Allows, a wrapped guard, StateMachineCoverage.Record) goes through here instead of GetType()
// directly, so both shapes are handled the same way without the printer/builder code above having
// to know which one it was given.

[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
internal static class StateMachineRuntime
{
	public static object Unwrap(object value)
	{
		var type = value.GetType();
		if (!StateMachineCases.IsUnion(type))
		{
			return value;
		}

		var valueProperty = type.GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
		return valueProperty?.GetValue(value) ?? value;
	}

	public static Type CaseTypeOf(object value) => Unwrap(value).GetType();

	/// <summary>The inverse of <see cref="Unwrap"/> — see <see cref="StateMachine{TState}.Wrap"/>'s own doc comment.</summary>
	public static TState Wrap<TState>(Type caseType, object caseInstance)
	{
		var stateType = typeof(TState);
		if (!StateMachineCases.IsUnion(stateType))
		{
			return (TState)caseInstance;
		}

		var constructor = stateType.GetConstructor([caseType]) ??
		throw new InvalidOperationException($"{stateType.Name} has no single-parameter constructor accepting {caseType.Name}.");
		return (TState)constructor.Invoke([caseInstance]);
	}
}

// ── StateMachineCoverage<TState> ──────────────────────────────────────────────

/// <summary>
/// Manual coverage tracking for a <see cref="StateMachine{TState}"/>: record every (from, to) pair
/// actually observed — e.g. from inside the <c>events:</c> handler passed to
/// <c>Script&lt;TSut&gt;.SampleWith</c> — and check <see cref="Uncovered"/> once a run finishes.
/// Not wired into <c>SampleWith</c> itself yet (a script has no notion of "the entity's state" to
/// diff before/after a step) — see the root README's "coming next" section for that gap.
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class StateMachineCoverage<TState>
{
	private readonly IReadOnlyList<StateMachineEdge<TState>> _edges;
	private readonly HashSet<(Type From, Type To, string Name)> _covered = [];

	internal StateMachineCoverage(IReadOnlyList<StateMachineEdge<TState>> edges) => _edges = edges;

	/// <summary>Marks the edge matching (from, to)'s runtime types as covered, if one exists.</summary>
	public void Record(TState from, TState to)
	{
		var fromType = StateMachineRuntime.CaseTypeOf(from!);
		var toType = StateMachineRuntime.CaseTypeOf(to!);
		foreach (var edge in _edges)
		{
			if (edge.From == fromType && edge.To == toType)
			{
				_covered.Add((edge.From, edge.To, edge.Name));
			}
		}
	}

	/// <summary>Every declared edge never passed to <see cref="Record"/>.</summary>
	public IReadOnlyList<StateMachineEdge<TState>> Uncovered => [.. _edges.Where(e => !_covered.Contains((e.From, e.To, e.Name)))];

	public bool FullyCovered => Uncovered.Count == 0;
}
