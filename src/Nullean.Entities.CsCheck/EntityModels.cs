// Nullean.Entities.CsCheck — ways to declare a state machine other than "an immutable record whose methods return the next value".
//
// Every front-end here produces an EntitySpec, so Exhaustive, Sample, Mermaid, Snapshot, Faults, Conform, Commutes and Idempotent work
// unchanged:
//
//   StateMachine<TState,TTrigger>.Spec()      a declared machine (states, triggers, edges): proves every edge and state reachable
//   machine.ConformTo(entity, ...)            walks the machine and checks a real entity follows it, step by step
//   EntitySpec.FromReducer(...)               State -> Command -> State (Elm, MVU, F# style)
//   EntitySpec.EventSourced(...)              Decide(state, command) -> events, Evolve(state, event) -> state; temporal rules over the events
//   EntitySpec.ForMutable(...)                a class that mutates in place: states are rebuilt by replaying the actions on a fresh instance

using CsCheck;

namespace Nullean.Entities.CsCheck;

// ── Declared machine ─────────────────────────────────────────────────────────

internal sealed class EntityHolder<TEntity>(TEntity entity)
{
	public TEntity Entity { get; set; } = entity;
}

public static class StateMachineSpecExtensions
{
	/// <summary>
	/// Explores the machine itself: one action per trigger (and per timeout), the declared edges as the rule, and every declared state as
	/// <c>Reachable</c>. <c>Exhaustive()</c> therefore proves each state can be reached and each edge taken, and fails naming the one that cannot.
	/// </summary>
	public static EntitySpec<TState> Spec<TState, TTrigger>(this StateMachine<TState, TTrigger> machine) where TState : notnull where TTrigger : notnull =>
		BuildSpec(machine, includeTimeouts: true);

	internal static EntitySpec<TState> BuildSpec<TState, TTrigger>(
		StateMachine<TState, TTrigger> machine,
		bool includeTimeouts
	) where TState : notnull where TTrigger : notnull
	{
		var rule = ConstraintSet.From(("DeclaredTransitions", machine.AsConstraint<TState>(s => s)));
		var actions = machine
			.Triggers
			.Select(
				trigger =>
					new EntityAction<TState>(
						Convert.ToString(trigger, System.Globalization.CultureInfo.InvariantCulture) ?? "trigger",
						null,
						(state, _) => machine.TryFire(state, trigger, out var next) ? next : state
					)
			)
			.ToList();
		if (includeTimeouts)
		{
			foreach (var timeout in machine.Timeouts)
			{
				var captured = timeout;
				actions.Add(
					new EntityAction<TState>(
						$"after {captured.After:c} in {captured.From}",
						null,
						(state, _) => EqualityComparer<TState>.Default.Equals(state, captured.From) ? captured.To : state
					)
				);
			}
		}
		var spec = EntitySpec.For(rule, new EntityActions<TState>([.. actions]), machine.Initial);
		foreach (var state in machine.States)
		{
			var wanted = state;
			spec.Reachable($"state {state} is reachable", s => EqualityComparer<TState>.Default.Equals(s, wanted));
		}
		return spec;
	}

	/// <summary>
	/// Walks the declared machine and applies every trigger to a real entity (<paramref name="bindings"/> say how), asserting after each step
	/// that the entity is in the state the machine says (<paramref name="stateOf"/>) and that a trigger the machine forbids leaves it unchanged.
	/// A trigger the machine declares but the entity cannot perform fails first, naming it: that is the machine finding a state the entity
	/// can never reach. Timeouts are explored by <c>Spec()</c> but not conformance-tested (they need a clock).
	/// </summary>
	public static SpecReport ConformTo<TState, TTrigger, TEntity>(
		this StateMachine<TState, TTrigger> machine,
		TEntity initial,
		Func<TEntity, TState> stateOf,
		Action<string>? writeLine = null,
		long iter = -1,
		params (TTrigger Trigger, Func<TEntity, TEntity> Apply)[] bindings
	) where TState : notnull where TTrigger : notnull
	{
		RequireBound(machine, bindings.Select(b => b.Trigger), stateOf(initial));
		var byName = bindings.ToDictionary(b => Convert.ToString(b.Trigger, System.Globalization.CultureInfo.InvariantCulture)!, b => b.Apply);
		return BuildSpec(machine, includeTimeouts: false)
			.Conform(
				() => new EntityHolder<TEntity>(initial),
				(holder, step) =>
				{
					holder.Entity = byName[step.Action](holder.Entity);
					var actual = stateOf(holder.Entity);
					return EqualityComparer<TState>.Default.Equals(actual, step.After)
						? null
						: $"after {step.Action} in {step.Before} the machine says {step.After}, but the entity is in {actual}";
				},
				writeLine,
				iter: iter
			);
	}

	/// <summary><see cref="ConformTo{TState, TTrigger, TEntity}"/> for a class that mutates in place: a fresh entity per walk from <paramref name="create"/>.</summary>
	public static SpecReport ConformToMutable<TState, TTrigger, TEntity>(
		this StateMachine<TState, TTrigger> machine,
		Func<TEntity> create,
		Func<TEntity, TState> stateOf,
		Action<string>? writeLine = null,
		long iter = -1,
		params (TTrigger Trigger, Action<TEntity> Apply)[] bindings
	) where TState : notnull where TTrigger : notnull
	{
		RequireBound(machine, bindings.Select(b => b.Trigger), stateOf(create()));
		var byName = bindings.ToDictionary(b => Convert.ToString(b.Trigger, System.Globalization.CultureInfo.InvariantCulture)!, b => b.Apply);
		return BuildSpec(machine, includeTimeouts: false)
			.Conform(
				() => new EntityHolder<TEntity>(create()),
				(holder, step) =>
				{
					byName[step.Action](holder.Entity);
					var actual = stateOf(holder.Entity);
					return EqualityComparer<TState>.Default.Equals(actual, step.After)
						? null
						: $"after {step.Action} in {step.Before} the machine says {step.After}, but the entity is in {actual}";
				},
				writeLine,
				iter: iter
			);
	}

	internal static void RequireBound<TState, TTrigger>(
		StateMachine<TState, TTrigger> machine,
		IEnumerable<TTrigger> bound,
		TState entityStart
	) where TState : notnull where TTrigger : notnull
	{
		var missing = machine.Triggers.Except(bound).ToList();
		if (missing.Count > 0)
		{
			throw new InvalidOperationException(
				$"The machine declares trigger(s) the entity has no binding for: {string.Join(", ", missing)}. Either the entity cannot do them (so the states they lead to are unreachable in practice) or the binding is missing."
			);
		}
		if (!EqualityComparer<TState>.Default.Equals(entityStart, machine.Initial))
		{
			throw new InvalidOperationException($"The entity starts in {entityStart}, but the machine starts in {machine.Initial}.");
		}
	}
}

// ── Reducer, event-sourced, mutable ──────────────────────────────────────────

/// <summary>A mutable entity as the immutable value CsCheck explores: the actions taken so far, replayed on a fresh instance when needed.</summary>
public sealed class Replayed<T>
{
	private readonly Context _context;
	private readonly IReadOnlyList<Action<T>> _path;

	internal sealed class Context(Func<T> factory, Func<T, object?> snapshot)
	{
		public Func<T> Factory { get; } = factory;

		public Func<T, object?> Snapshot { get; } = snapshot;
	}

	internal Replayed(Context context, IReadOnlyList<Action<T>> path, IReadOnlyList<string> names)
	{
		_context = context;
		_path = path;
		Names = names;
		var instance = context.Factory();
		foreach (var step in path)
		{
			step(instance);
		}
		Instance = instance;
		Key = context.Snapshot(instance);
	}

	/// <summary>The entity as it is after the actions in <see cref="Names"/>. Do not mutate it: it is rebuilt, not shared.</summary>
	public T Instance { get; }

	/// <summary>The names of the actions taken to get here.</summary>
	public IReadOnlyList<string> Names { get; }

	internal object? Key { get; }

	internal Replayed<T> Then(string name, Action<T> action) => new(_context, [.. _path, action], [.. Names, name]);

	public override string ToString() => $"{Instance}";
}

public static partial class EntitySpec
{
	/// <summary>
	/// State, command, new state: <c>(state, command) => state</c>, as in Elm, MVU or an F# fold. Each command in <paramref name="commands"/> is an
	/// action named by its <c>ToString()</c>.
	/// </summary>
	public static EntitySpec<TState> FromReducer<TState, TCommand>(
		TState initial,
		Func<TState, TCommand, TState> reduce,
		IEnumerable<TCommand> commands,
		ConstraintSet<TState>? constraints = null
	)
	{
		var actions = commands.Select(
			c =>
				new EntityAction<TState>(
					Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture) ?? typeof(TCommand).Name,
					null,
					(state, _) => reduce(state, c)
				)
		);
		return For(constraints ?? ConstraintSet.From<TState>(), new EntityActions<TState>([.. actions]), initial);
	}

	/// <summary>
	/// Decide/Evolve, the event-sourcing pattern: <paramref name="decide"/> turns a command into the events it produces from a state,
	/// <paramref name="evolve"/> folds an event into the state. State constraints apply as usual (<c>Exhaustive</c> proves them). Temporal rules over
	/// the emitted events are checked on random walks: <see cref="EventSourcedSpec{TState, TCommand, TEvent}.SampleEvents"/>.
	/// </summary>
	public static EventSourcedSpec<TState, TCommand, TEvent> EventSourced<TState, TCommand, TEvent>(
		TState initial,
		Func<TState, TCommand, IEnumerable<TEvent>> decide,
		Func<TState, TEvent, TState> evolve,
		IEnumerable<TCommand> commands,
		ConstraintSet<TState>? constraints = null
	)
	{
		var list = commands.ToList();
		var byName = list.ToDictionary(c => Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture) ?? typeof(TCommand).Name);
		var actions = list.Select(c =>
				new EntityAction<TState>(
					Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture) ?? typeof(TCommand).Name,
					null,
					(state, _) => decide(state, c).Aggregate(state, evolve)
				));
		var set = constraints ?? ConstraintSet.From<TState>();
		return new EventSourcedSpec<TState, TCommand, TEvent>(
			For(set, new EntityActions<TState>([.. actions]), initial),
			initial,
			set,
			decide,
			evolve,
			byName
		);
	}

	/// <summary>
	/// A class that mutates in place (an EF aggregate, a service). CsCheck needs immutable states, so each state is rebuilt by replaying the
	/// actions on a fresh instance from <paramref name="factory"/>; two states are the same when <paramref name="snapshot"/> says so (default:
	/// public properties, structurally). Add actions with <see cref="Do{T}(EntitySpec{Replayed{T}}, string, Action{T})"/>.
	/// </summary>
	public static EntitySpec<Replayed<T>> ForMutable<T>(Func<T> factory, ConstraintSet<T>? constraints = null, Func<T, object?>? snapshot = null)
	{
		var context = new Replayed<T>.Context(factory, snapshot ?? (instance => StructuralKey.Of(instance)));
		var lifted = (constraints ?? ConstraintSet.From<T>()).Map<Replayed<T>>(r => r.Instance);
		return new EntitySpec<Replayed<T>>(lifted, [new Replayed<T>(context, [], [])], null).Key(r => r.Key);
	}

	/// <summary>An action on a mutable entity, with no argument.</summary>
	public static EntitySpec<Replayed<T>> Do<T>(this EntitySpec<Replayed<T>> spec, string name, Action<T> action) =>
		spec.Action(name, r => r.Then(name, action));

	/// <summary>An action on a mutable entity over a finite set of arguments.</summary>
	public static EntitySpec<Replayed<T>> Do<T, TArg>(this EntitySpec<Replayed<T>> spec, string name, TArg[] domain, Action<T, TArg> action) =>
		spec.Action(name, domain, (r, arg) => r.Then($"{name}({arg})", t => action(t, arg)));
}

/// <summary>An event-sourced model: see <see cref="EntitySpec.EventSourced{TState, TCommand, TEvent}"/>.</summary>
public sealed class EventSourcedSpec<TState, TCommand, TEvent>
{
	private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private readonly TState _initial;
	private readonly ConstraintSet<TState> _constraints;
	private readonly Func<TState, TCommand, IEnumerable<TEvent>> _decide;
	private readonly Func<TState, TEvent, TState> _evolve;
	private readonly Dictionary<string, TCommand> _commands;

	internal EventSourcedSpec(
		EntitySpec<TState> spec,
		TState initial,
		ConstraintSet<TState> constraints,
		Func<TState, TCommand, IEnumerable<TEvent>> decide,
		Func<TState, TEvent, TState> evolve,
		Dictionary<string, TCommand> commands
	)
	{
		Spec = spec;
		_initial = initial;
		_constraints = constraints;
		_decide = decide;
		_evolve = evolve;
		_commands = commands;
	}

	/// <summary>The state machine over the folded state: <c>Exhaustive()</c>, <c>Mermaid()</c>, <c>Faults()</c> and the rest.</summary>
	public EntitySpec<TState> Spec { get; }

	/// <summary>
	/// Random walks of commands; the events each step emits are stamped on a virtual clock (<paramref name="stepInterval"/> apart) and checked against
	/// <paramref name="rules"/> with <see cref="Trace.Check{TEvent}"/>. A violation fails with the rule, its time and evidence, and a shrunk walk.
	/// </summary>
	public void SampleEvents(
		TemporalRuleSet<TEvent> rules,
		TimeSpan? stepInterval = null,
		int minSteps = 1,
		int maxSteps = 24,
		string? seed = null,
		long iter = -1
	)
	{
		var interval = stepInterval ?? TimeSpan.FromSeconds(1);
		var origin = Origin;
		Spec.SampleTraces(
			steps =>
			{
				var stamped = new List<Timestamped<TEvent>>();
				var at = origin;
				foreach (var step in steps)
				{
					at += interval;
					foreach (var e in _decide(step.Before, _commands[step.Action]))
					{
						stamped.Add(new Timestamped<TEvent>(at, e));
					}
				}
				var report = Trace.Check(stamped, rules, at);
				return report.Passed ? null : report.ToString();
			},
			minSteps,
			maxSteps,
			seed,
			iter
		);
	}

	/// <summary>
	/// Proves the temporal <paramref name="rules"/> over the emitted events for every command sequence, not a sample: explores the product of the folded
	/// state and every rule's monitor (each command advances a virtual clock by <paramref name="stepInterval"/>), so the state constraints and the
	/// rules are checked together and a violation comes with a shortest command sequence. Two states are the same when their folded state and
	/// every monitor's pending obligations (deadlines relative to now) are equal, which is what lets the search close.
	/// </summary>
	/// <remarks>
	/// Only rules that are decided by a prefix of the events can be proved this way: <c>Never</c>, <c>NeverTwice</c>, <c>Always</c>, <c>Transitions</c>,
	/// <c>When</c> and <c>After().Then().Within()</c>. <c>Eventually</c> and <c>Required()</c> only resolve when the stream ends, so a search that
	/// stops at a bound would call them violated for no reason; they are rejected, and belong in <see cref="SampleEvents"/>.
	/// Events are told apart by <c>ToString()</c>, so use records (or override it).
	/// </remarks>
	public SpecReport Exhaustive(
		TemporalRuleSet<TEvent> rules,
		TimeSpan? stepInterval = null,
		int maxStates = 100_000,
		int maxDepth = int.MaxValue,
		bool requireClosed = true,
		Action<string>? writeLine = null
	)
	{
		var interval = stepInterval ?? TimeSpan.FromSeconds(1);
		var monitors = new List<IMonitor<TEvent>>();
		foreach (var (name, rule) in rules.Rules)
		{
			var monitor = rule.CreateMonitor();
			if (monitor.StateKey(Origin) is null)
			{
				throw new NotSupportedException(
					$"Rule '{name}' only resolves when the event stream ends (Eventually, Required()), so it cannot be proved by exploring command sequences up to a bound. Check it with SampleEvents instead."
				);
			}
			monitors.Add(monitor);
		}

		var names = rules.Rules.Select(r => r.Name).ToList();
		var lifted = _constraints.Map<Observed<TState, TEvent>>(o => o.State);
		var temporal = new List<(string, Constraint<Observed<TState, TEvent>>)>();
		for (var index = 0; index < names.Count; index++)
		{
			var (i, name) = (index, names[index]);
			temporal.Add(($"temporal: {name}",
				Constraints<Observed<TState, TEvent>>.Check(o =>
						o.Monitors[i].Current.IsViolated
							? [
								new Violation($"{name} violated at {o.Monitors[i].Current.ViolationAt:HH:mm:ss}: {o.Monitors[i].Current.ViolationEvidence}")
								{
									Evidence = o.Monitors[i].Current.ViolationEvidence
								}
							]
							: [])));
		}
		var set = ConstraintSet.From([.. lifted.AsConstraints().Select(c => (c.Name ?? "constraint", c)), .. temporal]);

		var actions = _commands.Select(c =>
			new EntityAction<Observed<TState, TEvent>>(c.Key, null, (o, _) =>
			{
				var at = o.At + interval;
				List<IMonitor<TEvent>> next = [.. o.Monitors.Select(m => m.Clone())];
				var events = _decide(o.State, c.Value).ToList();
				foreach (var monitor in next)
				{
					foreach (var e in events)
					{
						monitor.Observe(e, at);
					}
					monitor.Tick(at);
				}
				return new Observed<TState, TEvent>(events.Aggregate(o.State, _evolve), next, at);
			}));

		return EntitySpec
			.For(set, new EntityActions<Observed<TState, TEvent>>([.. actions]), new Observed<TState, TEvent>(_initial, monitors, Origin))
			.Key(o => (StructuralKey.Of(o.State), string.Join("¦", o.Monitors.Select(m => m.StateKey(o.At)))))
			.Print(o => Convert.ToString(o.State, System.Globalization.CultureInfo.InvariantCulture) ?? "")
			.Exhaustive(maxStates, maxDepth, requireCoverage: false, requireClosed: requireClosed, writeLine: writeLine);
	}
}

/// <summary>The folded state plus every temporal monitor and the virtual clock: one node of the product explored by <c>EventSourcedSpec.Exhaustive</c>.</summary>
internal sealed record Observed<TState, TEvent>(TState State, IReadOnlyList<IMonitor<TEvent>> Monitors, DateTimeOffset At);
