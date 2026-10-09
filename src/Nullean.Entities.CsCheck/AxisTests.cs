// Nullean.Entities.CsCheck — one named test per axis, chosen from what the rules declare.
//
//   Invariant     01  Laws over the rules that must hold in every state
//   Idempotency   02  each Idempotent rule's action, applied twice, over generated values
//   Monotonicity  03  each Monotonic / MonotonicPerKey rule, from a valid start through every action
//   Conservation  04  each Conserves rule, from a valid start through every action
//   Confluence    08  each OrderIndependent rule, over shuffled orderings of the declared items
//   Vacancy       09  every rule can fire; every guard is reached
//
// Each method reads Constraint<T>.Shape, so the test it runs follows from the declaration, not from wiring. It returns an AxisResult
// (NotApplicable when no rule is on the axis, Skipped when it cannot be attacked with what was given) and throws on a failure.
// A green test that never reached its rule is a failure too: an axis that sees nothing move throws VacuousPropertyException.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public static class AxisTests
{
	/// <summary>
	/// Axis 01. The rules that must hold in every state hold for generated values, and each can fire: <c>Laws</c> over the invariant rules. A rule with
	/// no counter-example is not a failure here (that is axis 09, <see cref="Vacancy{T}"/>): a given counter-example is still checked to break only its rule.
	/// </summary>
	public static AxisResult Invariant<T>(
		this ConstraintSet<T> constraints,
		Gen<T> valid,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		AxisRun? run = null
	)
	{
		var settings = run ?? new AxisRun();
		var rules = constraints.OfAxis(Axis.Invariant);
		if (!constraints.AsConstraints().Any(c => c.Axis == Axis.Invariant))
		{
			return new AxisResult.NotApplicable(Axis.Invariant);
		}
		_ = rules.Laws(
			valid,
			Only(counterExamples, rules),
			settings.Samples,
			derivedFrom: Math.Min(25, settings.Samples),
			settings.GeneratorSeed,
			requireCounterExamples: false,
			time: settings.Time,
			writeLine: settings.WriteLine
		);
		return new AxisResult.Passed(Axis.Invariant, Names(constraints, Axis.Invariant), settings.Samples);
	}

	/// <summary>
	/// Axis 02. Every <c>Idempotent</c> rule's action, applied twice, gives what applying it once gives, over generated valid values. Vacuous when the action
	/// left every generated value unchanged (it was never exercised): the report says in how many of the samples it changed anything.
	/// </summary>
	public static AxisResult Idempotency<T>(this ConstraintSet<T> constraints, Gen<T>? valid = null, AxisRun? run = null)
	{
		var settings = run ?? new AxisRun();
		var declared = Rules(constraints, Axis.Idempotency);
		if (declared.Count == 0)
		{
			return new AxisResult.NotApplicable(Axis.Idempotency);
		}
		var rules = declared.Where(c => ((IdempotentShape<T>)c.Shape).Action is not null).ToList();
		if (rules.Count == 0)
		{
			return new AxisResult.Skipped(
				Axis.Idempotency,
				$"{string.Join(", ", declared.Select(Name))} was mapped without a way to put a changed value back: use Map(project, set)"
			);
		}

		var gen = valid ?? GenAuto.Of<T>(settings.Data).Valid(constraints, settings.Time);
		var changed = new long[rules.Count];
		gen.Sample(
			value =>
			{
				for (var i = 0; i < rules.Count; i++)
				{
					var action = ((IdempotentShape<T>)rules[i].Shape).Action!;
					var once = action(value);
					var twice = action(once);
					if (!EqualityComparer<T>.Default.Equals(once, twice))
					{
						throw new InvalidOperationException($"{Name(rules[i])} is not idempotent: once gave {Print(once)}, twice gave {Print(twice)}.");
					}
					if (!EqualityComparer<T>.Default.Equals(value, once))
					{
						_ = Interlocked.Increment(ref changed[i]);
					}
				}
			},
			settings.WriteLine,
			settings.EffectiveSeed,
			settings.Samples,
			print: Print
		);

		for (var i = 0; i < rules.Count; i++)
		{
			if (changed[i] == 0)
			{
				throw new VacuousPropertyException(
					$"Idempotency - {Name(rules[i])}: its action changed none of the {settings.Samples} generated values, so idempotency was never exercised."
				);
			}
		}
		return new AxisResult.Passed(
			Axis.Idempotency,
			[.. rules.Select(Name)],
			settings.Samples,
			$"(changed a value in {string.Join(", ", rules.Select((r, i) => $"{changed[i]}/{settings.Samples}"))})"
		);
	}

	/// <summary>
	/// Axis 03. From a valid start, every action (with generated arguments) leaves each <c>Monotonic</c> / <c>MonotonicPerKey</c> rule holding. Vacuous when no action
	/// ever moved the value (or, per key, when no key was present both before and after): a monotonic rule over a value nothing changes checks nothing.
	/// </summary>
	public static AxisResult Monotonicity<T>(this ConstraintSet<T> constraints, EntityActions<T> actions, Gen<T>? start = null, AxisRun? run = null) =>
		Monotonicity(constraints, ToAutoActions(actions, run), start, run);

	/// <inheritdoc cref="Monotonicity{T}(ConstraintSet{T}, EntityActions{T}, Gen{T}, AxisRun)"/>
	public static AxisResult Monotonicity<T>(
		this ConstraintSet<T> constraints,
		IReadOnlyList<AutoAction<T>> actions,
		Gen<T>? start = null,
		AxisRun? run = null
	) =>
		Sequential(constraints, Axis.Monotonicity, actions, start, run ?? new AxisRun());

	/// <summary>
	/// Axis 04. From a valid start, every action leaves each <c>Conserves</c> rule holding. Vacuous when no action ever changed the value at all.
	/// </summary>
	public static AxisResult Conservation<T>(this ConstraintSet<T> constraints, EntityActions<T> actions, Gen<T>? start = null, AxisRun? run = null) =>
		Conservation(constraints, ToAutoActions(actions, run), start, run);

	/// <inheritdoc cref="Conservation{T}(ConstraintSet{T}, EntityActions{T}, Gen{T}, AxisRun)"/>
	public static AxisResult Conservation<T>(
		this ConstraintSet<T> constraints,
		IReadOnlyList<AutoAction<T>> actions,
		Gen<T>? start = null,
		AxisRun? run = null
	) =>
		Sequential(constraints, Axis.Conservation, actions, start, run ?? new AxisRun());

	/// <summary>
	/// Axis 08. Each <c>OrderIndependent</c> rule: listing the value's items in a random order does not change the result. Orderings shrink toward the original
	/// order, so a failure is the smallest reordering that still differs. Vacuous when no generated value had two items to reorder.
	/// </summary>
	public static AxisResult Confluence<T>(this ConstraintSet<T> constraints, Gen<T>? valid = null, AxisRun? run = null)
	{
		var settings = run ?? new AxisRun();
		var declared = Rules(constraints, Axis.Confluence);
		if (declared.Count == 0)
		{
			return new AxisResult.NotApplicable(Axis.Confluence);
		}
		var rules = declared.Where(c => ((OrderIndependentShape<T>)c.Shape).Permute is not null).ToList();
		if (rules.Count == 0)
		{
			return new AxisResult.Skipped(
				Axis.Confluence,
				$"{string.Join(", ", declared.Select(Name))} was mapped without a way to put a changed value back: use Map(project, set)"
			);
		}

		var gen = valid ?? GenAuto.Of<T>(settings.Data).Valid(constraints, settings.Time);
		foreach (var rule in rules)
		{
			var shape = (OrderIndependentShape<T>)rule.Shape;
			var reordered = 0L;
			gen.SelectMany(value => Gen.Int.Array[shape.Count(value)].Select(keys => (Value: value, Order: Argsort(keys)))).Sample(
				x =>
				{
					var identity = x.Order.Select((o, i) => o == i).All(same => same);
					if (x.Order.Count >= 2 && !identity)
					{
						_ = Interlocked.Increment(ref reordered);
					}
					var permuted = shape.Permute!(x.Value, x.Order);
					if (!shape.SameResult(x.Value, permuted))
					{
						throw new InvalidOperationException(
							$"{Name(rule)}: listing the items of {Print(x.Value)} in the order [{string.Join(", ", x.Order)}] changed the result."
						);
					}
				},
				settings.WriteLine,
				settings.EffectiveSeed,
				settings.Samples,
				print: x => $"{Print(x.Value)} reordered as [{string.Join(", ", x.Order)}]"
			);
			if (reordered == 0)
			{
				throw new VacuousPropertyException(
					$"Confluence - {Name(rule)}: no generated value had two items to reorder in {settings.Samples} samples, so order independence was never exercised."
				);
			}
		}
		return new AxisResult.Passed(Axis.Confluence, [.. rules.Select(Name)], settings.Samples);
	}

	/// <summary>
	/// Axis 09. The tests reached the rules: every rule can fire (it has a counter-example that breaks it and only it, given or derived from an edge value; an idempotent or order-independent rule is exempt, its own axis proves it was exercised) and every
	/// <c>When(...).Requires(...)</c> guard is true for at least one generated value. A rule with no counter-example fails here, which is the point: nothing shows it can fire.
	/// </summary>
	public static AxisResult Vacancy<T>(
		this ConstraintSet<T> constraints,
		Gen<T> valid,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		AxisRun? run = null
	)
	{
		var settings = run ?? new AxisRun();
		var all = constraints.AsConstraints();
		if (all.Count == 0)
		{
			return new AxisResult.NotApplicable(Axis.Vacancy);
		}
		// A correct Idempotent or OrderIndependent rule cannot be broken by any value (that is what it says), so no counter-example can exist for it.
		// Its own axis shows it was exercised instead: the action changed values, items were reordered.
		var provenElsewhere = all.Where(c => c.Axis is Axis.Idempotency or Axis.Confluence).Select(c => c.Name).OfType<string>().ToArray();
		var attackable = provenElsewhere.Length == 0 ? constraints : constraints.Without(provenElsewhere);
		if (attackable.AsConstraints().Count > 0)
		{
			_ = attackable.Laws(
				valid,
				Only(counterExamples, attackable),
				settings.Samples,
				derivedFrom: Math.Min(25, settings.Samples),
				settings.GeneratorSeed,
				requireCounterExamples: true,
				time: settings.Time,
				writeLine: settings.WriteLine
			);
		}

		var guarded = all.Where(c => c.Guard is not null).ToList();
		var reached = new long[guarded.Count];
		if (guarded.Count > 0)
		{
			valid.Sample(
				value =>
				{
					for (var i = 0; i < guarded.Count; i++)
					{
						if (guarded[i].Guard!(value))
						{
							_ = Interlocked.Increment(ref reached[i]);
						}
					}
				},
				settings.WriteLine,
				settings.EffectiveSeed,
				settings.Samples,
				print: Print
			);
		}
		for (var i = 0; i < guarded.Count; i++)
		{
			if (reached[i] == 0)
			{
				throw new VacuousPropertyException(
					$"Vacancy - {Name(guarded[i])}: its guard was never true for the {settings.Samples} generated values, so the rule held without being exercised."
				);
			}
		}
		return new AxisResult.Passed(Axis.Vacancy, [.. all.Select(Name)], settings.Samples);
	}

	// ── 03 and 04: a sequential rule is attacked by running actions ──────────────

	private static AxisResult Sequential<T>(ConstraintSet<T> constraints, Axis axis, IReadOnlyList<AutoAction<T>> actions, Gen<T>? start, AxisRun settings)
	{
		var rules = Rules(constraints, axis);
		if (rules.Count == 0)
		{
			return new AxisResult.NotApplicable(axis);
		}
		if (actions.Count == 0)
		{
			return new AxisResult.Skipped(
				axis,
				$"{typeof(T).Name} has no actions to run {string.Join(", ", rules.Select(Name))} through: declare them ([Constraints] generates them from public methods that return {typeof(T).Name}) or pass an EntityActions"
			);
		}

		var checking = constraints.OfAxis(axis);
		var probes = rules.Select(r => ProbeOf(r.Shape)).ToArray();
		var compared = new long[rules.Count];
		var moved = new long[rules.Count];
		var starts = start ?? GenAuto.Of<T>(settings.Data).Valid(constraints, settings.Time);
		var runs = Gen.OneOf([.. actions.Select(a => a.Draw(starts).Select(drawn => (drawn.Start, Action: a, drawn.Argument)))]);
		runs.Sample(
			step =>
			{
				var next = step.Action.Apply(step.Start, step.Argument);
				var violations = ConstraintProperties.ViolationsOf(checking, step.Start, next, settings.Time);
				if (violations.Count > 0)
				{
					throw new InvalidOperationException(
						$"{step.Action.Name}({Print(step.Argument)}) from {Print(step.Start)} gave {Print(next)}, which violates: {string.Join("; ", violations.Select(v => $"{v.ConstraintId}: {v.Message}"))}"
					);
				}
				for (var i = 0; i < probes.Length; i++)
				{
					var probe = probes[i](step.Start, next);
					_ = Interlocked.Add(ref compared[i], probe.Compared);
					_ = Interlocked.Add(ref moved[i], probe.Moved);
				}
			},
			settings.WriteLine,
			settings.EffectiveSeed,
			settings.Samples,
			print: step => $"{Print(step.Start)} | {step.Action.Name}({Print(step.Argument)})"
		);

		for (var i = 0; i < rules.Count; i++)
		{
			if (moved[i] == 0)
			{
				var why = rules[i].Shape is MonotonicShape<T> { PerKey: true } && compared[i] == 0
					? "no key was present both before and after an action, so there was nothing to compare"
					: axis == Axis.Conservation ? "no action changed the value, so nothing was available to be conserved" : "no action changed the value";
				throw new VacuousPropertyException(
					$"{axis} - {Name(rules[i])}: {why} in {settings.Samples} runs, so the rule held without being exercised. Supply arguments or starts that reach it."
				);
			}
		}
		return new AxisResult.Passed(
			axis,
			[.. rules.Select(Name)],
			settings.Samples,
			axis == Axis.Conservation
				? $"(an action changed the value in {string.Join(", ", moved.Select(m => $"{m}"))} of {settings.Samples} runs; whether it touched what the rule sums cannot be told)"
				: $"(moved a value in {string.Join(", ", moved.Select(m => $"{m}"))} of {settings.Samples} runs)"
		);
	}

	private static IReadOnlyList<AutoAction<T>> ToAutoActions<T>(EntityActions<T> actions, AxisRun? run) =>
		[.. actions.Select(a => AutoAction.From(a, run?.Data ?? AutoMode.Mixed))];

	private static Func<T, T, ShapeProbe> ProbeOf<T>(ConstraintShape<T> shape) => shape switch
	{
		MonotonicShape<T> monotonic => monotonic.Probe,
		ConservesShape<T> => (before, after) => new ShapeProbe(1, EqualityComparer<T>.Default.Equals(before, after) ? 0 : 1),
		_ => (_, _) => default
	};

	// ── shared ───────────────────────────────────────────────────────────────────

	private static List<Constraint<T>> Rules<T>(ConstraintSet<T> constraints, Axis axis) => [.. constraints.AsConstraints().Where(c => c.Axis == axis)];

	private static string[] Names<T>(ConstraintSet<T> constraints, Axis axis) => [.. Rules(constraints, axis).Select(Name)];

	private static string Name<T>(Constraint<T> rule) => rule.Name ?? rule.Shape.Factory ?? "unnamed rule";

	private static string Print<T>(T value) => ConstraintProperties.SafePrint(value);

	// The order the items sort into when each is given a random key: a permutation of 0..n-1 that shrinks toward the original order (all keys equal).
	private static IReadOnlyList<int> Argsort(int[] keys) => [.. Enumerable.Range(0, keys.Length).OrderBy(i => keys[i]).ThenBy(i => i)];

	private static Dictionary<string, Func<T, T>>? Only<T>(IReadOnlyDictionary<string, Func<T, T>>? counterExamples, ConstraintSet<T> rules)
	{
		if (counterExamples is null)
		{
			return null;
		}
		var names = rules.AsConstraints().Select(c => c.Name).OfType<string>().ToHashSet();
		return counterExamples.Where(kv => names.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
	}
}
