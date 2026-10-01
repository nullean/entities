// Nullean.Entities.CsCheck — EntitySpec<T>: a ConstraintSet<T> plus an entity's actions, handed to
// CsCheck's Spec<S> engine.
//
// An entity here is an immutable value whose mutations return a new value (Order.Confirm()), i.e. a
// pure state machine. CsCheck's Spec<S> explores such machines exhaustively, by random walk, by
// mutation (Faults), and against a real implementation (Conform). This file translates the
// constraints into Spec requirements so each of those is one call:
//
//   PointInTime constraint        -> Invariant           (holds in every state)
//   Sequential constraint         -> Rule over each step (skipped when a step changed nothing,
//                                    matching ConstraintSet.Apply's Noop short-circuit)
//   When(guard).Requires(...)     -> Rule(when: guard)   (so "guard never true" is reported)
//   Transitions(...).Allow(a, b)  -> one Rule per legal edge  (so "edge never taken" is reported)

using System.Globalization;
using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>A state wrapper whose equality comes from a key projection, so CsCheck can tell when it has seen a state before.</summary>
internal readonly struct Keyed<T>(T value, Func<T, object?> key) : IEquatable<Keyed<T>>
{
	public T Value { get; } = value;

	public bool Equals(Keyed<T> other) => object.Equals(key(Value), key(other.Value));

	public override bool Equals(object? obj) => obj is Keyed<T> other && Equals(other);

	public override int GetHashCode() => key(Value)?.GetHashCode() ?? 0;
}

/// <summary>One step of a walk: the action taken, its argument, and the entity before and after.</summary>
public sealed record EntityStep<T>(string Action, string Argument, object? ArgumentValue, T Before, T After);

public static class ConstraintSetSpecExtensions
{
	/// <summary>
	/// Starts an <see cref="EntitySpec{T}"/>: the set's constraints become requirements over every state
	/// and step reachable from <paramref name="initial"/> through <paramref name="actions"/> (generated
	/// next to the constraint set as <c>Actions</c>).
	/// </summary>
	public static EntitySpec<T> Spec<T>(this ConstraintSet<T> constraints, T initial, EntityActions<T>? actions = null) => new(constraints, initial, actions);
}

public sealed class EntitySpec<T>
{
	private sealed record ActionDef(string Name, Type? ArgumentType, Func<T, object?, T> Apply, object[]? OwnDomain);

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private readonly ConstraintSet<T> _constraints;
	private readonly T _initial;
	private readonly List<ActionDef> _actions = [];
	private readonly Dictionary<Type, object[]> _domains = [];
	private readonly List<(string Id, Func<T, bool> Holds)> _reachable = [];
	private readonly List<(string Name, string? On, Func<T, T, T> Perturb)> _faults = [];
	private readonly List<(string Action, ConstraintSet<T> Set)> _scoped = [];
	private Func<T, object?> _key = v => v;
	private Func<T, string> _print = v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "null";
	private Func<T, bool>? _boundary;
	private Func<T, bool>? _terminal;
	private TimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

	internal EntitySpec(ConstraintSet<T> constraints, T initial, EntityActions<T>? actions)
	{
		_constraints = constraints;
		_initial = initial;
		if (actions is not null)
		{
			foreach (var a in actions)
			{
				_actions.Add(new ActionDef(a.Name, a.ArgumentType, a.Apply, null));
			}
		}
	}

	/// <summary>An action with no argument.</summary>
	public EntitySpec<T> Action(string name, Func<T, T> apply)
	{
		_actions.Add(new ActionDef(name, null, (s, _) => apply(s), null));
		return this;
	}

	/// <summary>An action over a small, finite set of arguments — each is tried from every state.</summary>
	public EntitySpec<T> Action<TArg>(string name, TArg[] domain, Func<T, TArg, T> apply)
	{
		_actions.Add(new ActionDef(name, typeof(TArg), (s, a) => apply(s, (TArg)a!), [.. domain.Cast<object>()]));
		return this;
	}

	/// <summary>
	/// The values every generated action taking a <typeparamref name="TArg"/> is tried with. Keep it
	/// small (two or three values): the reachable state space grows with it.
	/// </summary>
	public EntitySpec<T> Domain<TArg>(params TArg[] values)
	{
		_domains[typeof(TArg)] = [.. values.Cast<object>()];
		return this;
	}

	/// <summary>
	/// What makes two entities "the same state". The default is <c>Equals</c>, which never closes the
	/// state space for a record holding a list (list equality is by reference). Project to something
	/// comparable: <c>.Key(o => (o.Status, string.Join(",", o.Lines)))</c>.
	/// </summary>
	public EntitySpec<T> Key(Func<T, object?> key)
	{
		_key = key;
		return this;
	}

	/// <summary>How a state is printed in traces and diagrams.</summary>
	public EntitySpec<T> Print(Func<T, string> print)
	{
		_print = print;
		return this;
	}

	/// <summary>"Now" for time-aware constraints. Fixed, so exploration is deterministic.</summary>
	public EntitySpec<T> Clock(DateTimeOffset now)
	{
		_clock = new FixedTimeProvider(now);
		return this;
	}

	/// <summary>Stop expanding states outside this region, so an unbounded model can still close.</summary>
	public EntitySpec<T> Boundary(Func<T, bool> inside)
	{
		_boundary = inside;
		return this;
	}

	/// <summary>States where stopping is expected (not a deadlock).</summary>
	public EntitySpec<T> Terminal(Func<T, bool> isTerminal)
	{
		_terminal = isTerminal;
		return this;
	}

	/// <summary>Must hold in at least one reachable state — proves the model can get there.</summary>
	public EntitySpec<T> Reachable(string id, Func<T, bool> holds)
	{
		_reachable.Add((id, holds));
		return this;
	}

	/// <summary>A deliberate defect for <see cref="Faults"/>: replaces the result of <paramref name="on"/> (or every action when null).</summary>
	public EntitySpec<T> Fault(string name, string? on, Func<T, T, T> perturb)
	{
		_faults.Add((name, on, perturb));
		return this;
	}

	/// <summary>
	/// Checks <paramref name="constraints"/> only on steps taken by <paramref name="action"/> — for a
	/// <c>Monotonic</c>/<c>Conserves</c> rule that holds for one action but not for the type as a whole.
	/// </summary>
	public EntitySpec<T> StepScoped(string action, ConstraintSet<T> constraints)
	{
		_scoped.Add((action, constraints));
		return this;
	}

	// ── Terminals ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Proves every constraint holds in every reachable state and step. Fails with a shortest-path
	/// counterexample, and — with <paramref name="requireCoverage"/> — also when a <c>When</c> guard or
	/// an <c>Allow</c>ed transition is never exercised, since the constraint then proved nothing.
	/// </summary>
	public SpecReport Exhaustive(
		int maxStates = 10_000_000,
		int maxDepth = int.MaxValue,
		bool requireCoverage = true,
		bool requireClosed = true,
		Action<string>? writeLine = null
	)
	{
		CheckInitialState();
		var (spec, _) = Build(autoFaults: false);
		var report = spec.Exhaustive(maxStates, maxDepth, writeLine: writeLine);
		if (requireClosed && !report.Closed)
		{
			throw new InvalidOperationException(
				$"The state space did not close ({report.States} states).{Environment.NewLine}Declare a .Key(...) that identifies equal states, a .Boundary(...), or use .Sample() instead.{Environment.NewLine}{report}"
			);
		}
		var missed = report.NeverTriggered.Concat(report.NeverFired).ToList();
		if (requireCoverage && missed.Count > 0)
		{
			throw new VacuousPropertyException(
				$"Every constraint held, but never exercised: {string.Join(", ", missed)}.{Environment.NewLine}Add actions or domain values that reach them, or pass requireCoverage: false.{Environment.NewLine}{report}"
			);
		}
		return report;
	}

	/// <summary>Random walks with shrinking — for a state space too large to close.</summary>
	public SpecReport Sample(
		Action<string>? writeLine = null,
		int minSteps = 1,
		int maxSteps = 24,
		string? seed = null,
		long iter = -1,
		int time = -1,
		int threads = -1
	)
	{
		CheckInitialState();
		return Build(autoFaults: false).Spec.Sample(writeLine, minSteps, maxSteps, seed, iter, time, threads);
	}

	/// <summary>The reachable state graph as a Mermaid flowchart.</summary>
	public string Mermaid(int maxStates = 200) => Build(autoFaults: false).Spec.Mermaid(maxStates);

	/// <summary>
	/// Behavioural regression guard: hashes the reachable state graph. Pass <c>0</c> the first time and
	/// the failure message shows the value to commit; it changes only when an action or a constraint
	/// changes what is reachable.
	/// </summary>
	public void Snapshot(long expected, int maxStates = 200)
	{
		var diagram = Mermaid(maxStates);
		Check.Hash(h => h.Add(diagram), expected);
	}

	/// <summary>
	/// Mutation-tests the constraints. Each declared <see cref="Fault"/> (or, when none are declared, "this
	/// action has no effect" for every action) is injected in turn; a fault no constraint catches means a
	/// constraint is missing. Throws on uncaught faults when you declared faults; with the automatic ones
	/// it only reports, since an action that legitimately changes nothing is not a gap.
	/// </summary>
	public SpecFaultsReport Faults(Action<string>? writeLine = null, int maxStates = 10_000_000, bool? throwOnUncaught = null)
	{
		CheckInitialState();
		var (spec, _) = Build(autoFaults: true);
		return spec.Faults(writeLine, maxStates, throwOnUncaught: throwOnUncaught ?? (_faults.Count > 0));
	}

	/// <summary>Like <see cref="Faults"/>, sampled — for a state space <see cref="Exhaustive"/> cannot close.</summary>
	public SpecFaultsReport SampleFaults(Action<string>? writeLine = null, string? seed = null, long iter = -1, bool? throwOnUncaught = null)
	{
		CheckInitialState();
		var (spec, _) = Build(autoFaults: true);
		return spec.SampleFaults(writeLine, seed: seed, iter: iter, throwOnUncaught: throwOnUncaught ?? (_faults.Count > 0));
	}

	/// <summary>
	/// Drives a real implementation with the same walks and, after every step, asks it whether it agrees
	/// with the entity: <paramref name="apply"/> returns null when it does, otherwise a description of the divergence.
	/// </summary>
	public SpecReport Conform<TSut>(
		Func<TSut> create,
		Func<TSut, EntityStep<T>, string?> apply,
		Action<string>? writeLine = null,
		int minSteps = 1,
		int maxSteps = 24,
		string? seed = null,
		long iter = -1,
		int time = -1,
		int threads = -1
	)
	{
		var (spec, domains) = Build(autoFaults: false);
		return spec.Conform(
			create,
			(sut, t) => apply(sut, ToStep(t, domains)),
			writeLine,
			minSteps,
			maxSteps,
			seed,
			iter,
			time,
			threads
		);
	}

	/// <summary>
	/// Shorthand for <see cref="Conform{TSut}(Func{TSut}, Func{TSut, EntityStep{T}, string}, Action{string}, int, int, string, long, int, int)"/>:
	/// <paramref name="apply"/> performs the step on the implementation, then <paramref name="agrees"/> compares it to the entity's new state.
	/// </summary>
	public SpecReport Conform<TSut>(
		Func<TSut> create,
		Action<TSut, EntityStep<T>> apply,
		Func<TSut, T, bool> agrees,
		Action<string>? writeLine = null,
		long iter = -1
	) =>
		Conform(
			create,
			(sut, step) =>
			{
				apply(sut, step);
				return agrees(sut, step.After)
					? null
					: $"implementation disagrees with the entity after {step.Action}({step.Argument}); expected {_print(step.After)}";
			},
			writeLine,
			iter: iter
		);

	/// <summary>The two actions give the same entity in either order, from every reachable state and for every argument pair.</summary>
	public void Commutes(string first, string second, long iter = -1, string? seed = null)
	{
		var (spec, domains) = Build(autoFaults: false);
		var (a, ia) = Find(first);
		var (b, ib) = Find(second);
		var gen = Gen.Select(StateGen(spec), ArgGen(domains[ia]), ArgGen(domains[ib]), (s, x, y) => (s, x, y));
		gen.Sample(
			t => Same(Run(Run(t.s, a, t.x), b, t.y), Run(Run(t.s, b, t.y), a, t.x)),
			seed: seed,
			iter: iter,
			print: t => $"from {_print(t.s)}: {first}({t.x}) then {second}({t.y}) differs from the reverse order"
		);
	}

	/// <summary>Applying the action twice (same argument) gives the same entity as once, from every reachable state.</summary>
	public void Idempotent(string action, long iter = -1, string? seed = null)
	{
		var (spec, domains) = Build(autoFaults: false);
		var (a, ia) = Find(action);
		var gen = Gen.Select(StateGen(spec), ArgGen(domains[ia]), (s, x) => (s, x));
		gen.Sample(
			t => Same(Run(Run(t.s, a, t.x), a, t.x), Run(t.s, a, t.x)),
			seed: seed,
			iter: iter,
			print: t => $"from {_print(t.s)}: {action}({t.x}) is not idempotent"
		);
	}

	// ── Internals ─────────────────────────────────────────────────────────────

	private Keyed<T> Wrap(T value) => new(value, _key);

	private bool Same(T a, T b) => Wrap(a).Equals(Wrap(b));

	private static T Run(T state, ActionDef action, object? argument) => action.Apply(state, argument);

	private (ActionDef Action, int Index) Find(string name)
	{
		var index = _actions.FindIndex(a => a.Name == name);
		return index < 0
			? throw new ArgumentException($"No action named \"{name}\". Known: {string.Join(", ", _actions.Select(a => a.Name))}.", nameof(name))
			: (_actions[index], index);
	}

	private static Gen<object?> ArgGen(object[]? domain) => domain is null ? Gen.Const(() => (object?)null) : Gen.OneOfConst(domain).Select(o => (object?)o);

	private static Gen<T> StateGen(Spec<Keyed<T>> spec) =>
		spec.GenTrace(0, 12).Select(trace => trace.Steps.Length == 0 ? trace.Initial.Value : trace.Steps[^1].After.Value);

	private static EntityStep<T> ToStep(Transition<Keyed<T>> t, object[]?[] domains) =>
		new(t.Action, t.Arg, domains[t.ActionIndex] is { } d && t.ArgIndex >= 0 && t.ArgIndex < d.Length ? d[t.ArgIndex] : null, t.Before.Value, t.After.Value);

	private void CheckInitialState()
	{
		var violations = _constraints.Validate(_initial, _clock);
		if (violations.Count > 0)
		{
			throw new InvalidOperationException(
				$"The initial state already violates: {string.Join("; ", violations.Select(v => $"{v.ConstraintId}: {v.Message}"))}"
			);
		}
	}

	private (Spec<Keyed<T>> Spec, object[]?[] Domains) Build(bool autoFaults)
	{
		var spec = global::CsCheck.Spec.From(Wrap(_initial));
		spec.Print(s => _print(s.Value));
		if (_boundary is not null)
		{
			spec.Boundary(s => _boundary(s.Value));
		}
		if (_terminal is not null)
		{
			spec.Terminal(s => _terminal(s.Value));
		}

		var domains = new object[]?[_actions.Count];
		for (var i = 0; i < _actions.Count; i++)
		{
			var def = _actions[i];
			if (def.ArgumentType is null)
			{
				spec.Action(def.Name, s => Wrap(def.Apply(s.Value, null)));
				continue;
			}
			var domain = def.OwnDomain
			?? (_domains.TryGetValue(def.ArgumentType, out var shared)
				? shared
				: throw new InvalidOperationException(
					$"Action \"{def.Name}\" takes a {def.ArgumentType.Name}; declare the values to try with .Domain<{def.ArgumentType.Name}>(...)."
				));
			domains[i] = domain;
			spec.Action(def.Name, domain, (s, a) => Wrap(def.Apply(s.Value, a)));
		}

		var index = 0;
		foreach (var c in _constraints.AsConstraints())
		{
			AddRequirement(spec, c.Name ?? $"constraint{index}", c);
			index++;
		}
		foreach (var (action, set) in _scoped)
		{
			foreach (var c in set.AsConstraints())
			{
				var name = $"{c.Name ?? "constraint"}@{action}";
				spec.Rule(
					name,
					name,
					action,
					(before, after) => (c.Kind == ConstraintKind.Sequential && before.Equals(after)) || Holds(c, before.Value, after.Value)
				);
			}
		}
		foreach (var (id, holds) in _reachable)
		{
			spec.Reachable(id, id, s => holds(s.Value));
		}

		foreach (var (name, on, perturb) in _faults)
		{
			if (on is null)
			{
				spec.Fault(name, (_, _) => true, (before, after) => Wrap(perturb(before.Value, after.Value)));
			}
			else
			{
				spec.Fault(name, on, (before, after) => Wrap(perturb(before.Value, after.Value)));
			}
		}
		if (autoFaults && _faults.Count == 0)
		{
			foreach (var def in _actions)
			{
				spec.Fault($"{def.Name} has no effect", def.Name, (before, _) => before);
			}
		}
		return (spec, domains);
	}

	private bool Holds(Constraint<T> c, T before, T after) => !c.CheckAt(before, after, _clock).Any();

	private void AddRequirement(Spec<Keyed<T>> spec, string name, Constraint<T> c)
	{
		if (c.Kind == ConstraintKind.PointInTime)
		{
			if (c.Guard is { } guard)
			{
				// A When(...).Requires(...) rule: tie it to the guard so "never triggered" is visible.
				spec.Rule(name, name, (_, next) => guard(next.Value), (_, next) => Holds(c, next.Value, next.Value));
			}
			else
			{
				spec.Invariant(name, name, s => Holds(c, s.Value, s.Value));
			}
			return;
		}

		// Sequential: a step that changed nothing is a Noop and is not checked, as in ConstraintSet.Apply.
		spec.Rule(name, name, (before, after) => before.Equals(after) || Holds(c, before.Value, after.Value));
		if (c.StateOf is { } stateOf && c.Edges is { } edges)
		{
			foreach (var (from, to) in edges)
			{
				var edge = $"{name}:{from}->{to}";
				spec.Rule(edge, edge, (before, after) => stateOf(before.Value) == from && stateOf(after.Value) == to, (_, _) => true);
			}
		}
	}
}
