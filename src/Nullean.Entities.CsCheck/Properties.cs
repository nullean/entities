// Nullean.Entities.CsCheck — "simple" property tests from a ConstraintSet<T>: no state machine, no
// setup. Values come from AutoBogus (reproducible, shrinkable — see GenAuto) and the constraints are
// the oracle.
//
//   ForAll          a property over generated valid values
//   Preserves       every action, from a valid start, leaves the entity valid
//   Probe           which constraints never fail / never hold on generated values
//   RejectsInvalid  values that violate a constraint are named and rejected

using System.Globalization;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

public static class ConstraintProperties
{
	/// <summary>
	/// <paramref name="property"/> holds for every generated value that satisfies <paramref name="constraints"/>.
	/// Without <paramref name="gen"/> the values are AutoBogus-generated and filtered (see <c>Valid</c>); pass
	/// <c>spec.States()</c> for entities whose valid values are easier to reach by running actions than to filter for.
	/// </summary>
	public static void ForAll<T>(
		this ConstraintSet<T> constraints,
		Func<T, bool> property,
		Gen<T>? gen = null,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null
	) => (gen ?? GenAuto.Of<T>().Valid(constraints, time)).Sample(property, writeLine, seed, iter, print: SafePrint);

	/// <inheritdoc cref="ForAll{T}(ConstraintSet{T}, Func{T, bool}, Gen{T}, TimeProvider, string, long, Action{string})"/>
	public static void ForAll<T>(
		this ConstraintSet<T> constraints,
		Action<T> property,
		Gen<T>? gen = null,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null
	) => (gen ?? GenAuto.Of<T>().Valid(constraints, time)).Sample(property, writeLine, seed, iter, print: SafePrint);

	/// <summary>
	/// From a valid starting entity, any action with any generated argument leaves it satisfying the
	/// constraints: point-in-time ones on the result, sequential ones over (start, result). The action is
	/// run raw — <c>Apply</c> would reject a bad result and so could never reveal one. This is the
	/// randomised cousin of <c>EntitySpec.Exhaustive</c>, and because starts are AutoBogus values rather than
	/// <c>New()</c> it also reaches states the factory never produces. Starts and arguments are <see cref="AutoMode.Mixed"/> by
	/// default (some edge values); pass <paramref name="data"/> to change that for an entity not yet hardened against extremes.
	/// </summary>
	public static void Preserves<T>(
		this ConstraintSet<T> constraints,
		EntityActions<T> actions,
		Gen<T>? start = null,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null,
		AutoMode data = AutoMode.Mixed
	)
	{
		if (actions.Count == 0)
		{
			throw new InvalidOperationException(
				$"{typeof(T).Name} has no actions to check; [Constraints] generates them from public methods that return {typeof(T).Name}."
			);
		}
		var starts = start ?? GenAuto.Of<T>(data).Valid(constraints, time);
		var runs = Gen.OneOf([
			.. actions.Select(a => Gen.Select(starts, AutoDefaults.ArgumentGen(a.ArgumentType, data), (s, x) => (Start: s, Action: a, Argument: x)))
		]);
		runs.Sample(
			run =>
			{
				var next = run.Action.Apply(run.Start, run.Argument);
				var violations = ViolationsOf(constraints, run.Start, next, time);
				if (violations.Count > 0)
				{
					throw new InvalidOperationException(
						$"{run.Action.Name}({run.Argument}) from {run.Start} gave {next}, which violates: {string.Join("; ", violations.Select(v => $"{v.ConstraintId}: {v.Message}"))}"
					);
				}
			},
			writeLine,
			seed,
			iter,
			print: run => $"{SafePrint(run.Start)} | {run.Action.Name}({SafePrint(run.Argument)})"
		);
	}

	/// <summary>
	/// Samples raw AutoBogus values and counts, per point-in-time constraint, how often it held. A
	/// constraint that <b>never fails</b> is worth a look (a tautology, or a value that is derived and so cannot
	/// be wrong); one that <b>never holds</b> means no generated value is valid — a contradiction, or a value
	/// the generator cannot build — and throws unless <paramref name="throwOnContradiction"/> is false.
	/// </summary>
	public static ProbeReport Probe<T>(
		this ConstraintSet<T> constraints,
		Gen<T>? gen = null,
		int samples = 1000,
		bool throwOnContradiction = true,
		TimeProvider? time = null,
		Action<string>? writeLine = null
	)
	{
		var names = PointInTimeNames(constraints);
		var violated = names.ToDictionary(n => n, _ => 0);
		var reach = new ReachMeter();
		Sample(gen, samples, value =>
		{
			// The first few values are measured (a reflection walk of every one would cost more than the probe), enough to say how big the generator goes.
			if (reach.Measured < 300)
			{
				reach.Add(value);
			}
			// Once per value: a Check constraint can report several violations for one bad value.
			foreach (var id in SafeValidate(constraints, value, time).Select(v => v.ConstraintId).Where(id => id is not null).Distinct())
			{
				if (violated.TryGetValue(id!, out var count))
				{
					violated[id!] = count + 1;
				}
			}
		});
		var report = new ProbeReport(samples, [.. names.Select(n => new ProbeEntry(n, samples - violated[n], violated[n]))], reach.Result);
		writeLine?.Invoke(report.ToString());
		if (throwOnContradiction && report.NeverHeld.Count > 0)
		{
			throw new InvalidOperationException(
				$"No generated value satisfied: {string.Join(", ", report.NeverHeld)}. Either the constraint contradicts another, or the generator cannot build a valid value.{Environment.NewLine}{report}"
			);
		}
		return report;
	}

	/// <summary>
	/// For each point-in-time constraint, finds generated values that violate it and checks the
	/// constraint is <b>named</b> in <c>Validate</c> and that <c>Apply</c> to such a value is rejected naming it,
	/// leaving the entity unchanged. Constraints the generator never manages to violate are reported, not
	/// failed — often that is a derived value, sometimes a generator that only produces nice data.
	/// </summary>
	public static RejectionReport RejectsInvalid<T>(
		this ConstraintSet<T> constraints,
		Gen<T>? raw = null,
		string? constraint = null,
		int attempts = 2000,
		TimeProvider? time = null,
		Action<string>? writeLine = null
	)
	{
		var names = PointInTimeNames(constraints).Where(n => constraint is null || n == constraint).ToList();
		var current = TryValidStart(constraints, time);
		var checkedCount = names.ToDictionary(n => n, _ => 0);
		var failures = new List<string>();
		var seenAll = false;
		Sample(raw, attempts, value =>
		{
			if (seenAll || (current is not null && EqualityComparer<T>.Default.Equals(value, current.Value.Value)))
			{
				return;
			}
			var validate = SafeValidate(constraints, value, time);
			foreach (var name in names.Where(n => validate.Any(v => v.ConstraintId == n) && checkedCount[n] < 3))
			{
				checkedCount[name]++;
				// Without a valid entity to start from there is nothing for Apply to protect; naming in Validate is still checked.
				if (current is null)
				{
					continue;
				}
				var apply = constraints.Apply(current.Value.Value, _ => value, time);
				if (apply is not Result<T>.Fail fail || !fail.Violations.Any(v => v.ConstraintId == name))
				{
					failures.Add($"{name}: {value} violates it, but Apply did not reject the change naming it");
				}
			}
			seenAll = names.All(n => checkedCount[n] >= 3);
		});
		var report = new RejectionReport([.. names.Select(n => new RejectionEntry(n, checkedCount[n]))]);
		writeLine?.Invoke(report.ToString());
		return failures.Count > 0 ? throw new InvalidOperationException(string.Join(Environment.NewLine, failures)) : report;
	}

	// ── Internals ─────────────────────────────────────────────────────────────

	private static (T Value, bool Present)? TryValidStart<T>(ConstraintSet<T> constraints, TimeProvider? time)
	{
		try
		{
			return (AutoDefaults.ValidStarts(constraints, time, count: 1)[0], true);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static List<string> PointInTimeNames<T>(ConstraintSet<T> constraints)
	{
		List<string> names = [.. constraints.AsConstraints().Where(c => c.Kind == ConstraintKind.PointInTime).Select((c, i) => c.Name ?? $"constraint{i}")];
		if (names.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
		{
			throw new InvalidOperationException(
				$"Two constraints are named '{duplicate.Key}'. Probe and Laws tell rules apart by name: name them uniquely (Named), or pass a prefix where two sets are lifted into one (set.Map(project, prefix: \"Catalog.\"))."
			);
		}
		return names;
	}

	private static void Sample<T>(Gen<T>? gen, int count, Action<T> each)
	{
		var source = gen ?? AutoDefaults.Hostile<T>();
		try
		{
			for (var i = 0; i < count; i++)
			{
				each(source.Generate(new PCG(3u, (ulong)i + 1), null, out _));
			}
		}
		finally
		{
			(source as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// <c>Validate</c>, except a constraint that <b>throws</b> on the value is reported as a violation ("X threw OverflowException: ...")
	/// instead of aborting the run. Hostile data is exactly what makes constraints throw (an overflow in a computed property,
	/// <c>ToDictionary</c> on a duplicate id); a rule that crashes instead of reporting is a finding, not a reason to stop.
	/// </summary>
	internal static IReadOnlyList<Violation> SafeValidate<T>(ConstraintSet<T> constraints, T value, TimeProvider? time)
	{
		time ??= TimeProvider.System;
		var violations = new List<Violation>();
		var index = 0;
		foreach (var c in constraints.AsConstraints())
		{
			var name = c.Name ?? $"constraint{index++}";
			if (c.Kind != ConstraintKind.PointInTime)
			{
				continue;
			}
			try
			{
				violations.AddRange(c.CheckAt(value, value, time).Select(v => v with { ConstraintId = name }));
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				violations.Add(Violation.Error($"{name} threw {e.GetType().Name}: {e.Message}") with { ConstraintId = name });
			}
		}
		return violations;
	}

	/// <summary>A value rendered for a failure message, even when its <c>ToString</c> throws (a computed property that overflows).</summary>
	internal static string SafePrint<T>(T value)
	{
		try
		{
			return Check.Print(value);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return $"{typeof(T).Name} (ToString threw {e.GetType().Name}: {e.Message})";
		}
	}

	internal static IReadOnlyList<Violation> ViolationsOf<T>(ConstraintSet<T> constraints, T before, T after, TimeProvider? time)
	{
		time ??= TimeProvider.System;
		var unchanged = EqualityComparer<T>.Default.Equals(before, after);
		var violations = new List<Violation>();
		var index = 0;
		foreach (var c in constraints.AsConstraints())
		{
			var name = c.Name ?? $"constraint{index++}";
			if (c.Kind != ConstraintKind.PointInTime && unchanged)
			{
				continue;
			}
			try
			{
				violations.AddRange(c.CheckAt(before, after, time).Select(v => v with { ConstraintId = name }));
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				violations.Add(Violation.Error($"{name} threw {e.GetType().Name}: {e.Message}") with { ConstraintId = name });
			}
		}
		return violations;
	}
}

public sealed record ProbeEntry(string Constraint, int Held, int Violated);

public sealed class ProbeReport(int samples, IReadOnlyList<ProbeEntry> entries, GeneratedReach? reach = null)
{
	public int Samples { get; } = samples;

	/// <summary>
	/// How large and how deep the generated values were. A rule about more items, or more levels, than this cannot have been violated: it is not a tautology, the generator never got there
	/// (<c>AutoOptions.MaxCollection</c> and <c>MaxDepth</c>; a collection and its element count as two levels). Null when the report was built without measuring.
	/// </summary>
	public GeneratedReach? Reach { get; } = reach;

	public IReadOnlyList<ProbeEntry> Entries { get; } = entries;

	/// <summary>Held every time: a tautology for this generator, or a derived value.</summary>
	public IReadOnlyList<string> NeverViolated => [.. Entries.Where(e => e.Violated == 0).Select(e => e.Constraint)];

	/// <summary>Never held: a contradiction, or a value this generator cannot build.</summary>
	public IReadOnlyList<string> NeverHeld => [.. Entries.Where(e => e.Held == 0).Select(e => e.Constraint)];

	public override string ToString()
	{
		var sb = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"Probe over {Samples} generated values:");
		foreach (var e in Entries)
		{
			var note = e.Violated == 0 ? "  <- never violated" : e.Held == 0 ? "  <- never held" : "";
			sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Constraint,-40} held {e.Held,6}  violated {e.Violated,6}{note}");
		}
		if (Reach is { } measured)
		{
			sb.AppendLine(
				CultureInfo.InvariantCulture,
				$"  The generated values went up to {measured.LargestCollection} items in a collection and {measured.Depth} levels deep."
			);
			if (NeverViolated.Count > 0)
			{
				sb.AppendLine(
					"  A rule that never fails may be about more than that: raise AutoOptions.MaxCollection or MaxDepth (a collection and its element count as two levels) before calling it a tautology."
				);
			}
		}
		return sb.ToString();
	}
}

public sealed record RejectionEntry(string Constraint, int Checked);

public sealed class RejectionReport(IReadOnlyList<RejectionEntry> entries)
{
	public IReadOnlyList<RejectionEntry> Entries { get; } = entries;

	/// <summary>The generator never produced a value that violates these.</summary>
	public IReadOnlyList<string> Unfalsifiable => [.. Entries.Where(e => e.Checked == 0).Select(e => e.Constraint)];

	public override string ToString()
	{
		var sb = new StringBuilder().AppendLine("Rejection check:");
		foreach (var e in Entries)
		{
			sb.AppendLine(
				CultureInfo.InvariantCulture,
				$"  {e.Constraint,-40} {(e.Checked == 0 ? "no violating value generated" : $"{e.Checked} violating values rejected")}"
			);
		}
		return sb.ToString();
	}
}
