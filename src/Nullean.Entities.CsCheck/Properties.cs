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
	) => (gen ?? GenAuto.Of<T>().Valid(constraints, time)).Sample(property, writeLine, seed, iter);

	/// <inheritdoc cref="ForAll{T}(ConstraintSet{T}, Func{T, bool}, Gen{T}, TimeProvider, string, long, Action{string})"/>
	public static void ForAll<T>(
		this ConstraintSet<T> constraints,
		Action<T> property,
		Gen<T>? gen = null,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null
	) => (gen ?? GenAuto.Of<T>().Valid(constraints, time)).Sample(property, writeLine, seed, iter);

	/// <summary>
	/// From a valid starting entity, any action with any generated argument leaves it satisfying the
	/// constraints: point-in-time ones on the result, sequential ones over (start, result). The action is
	/// run raw — <c>Apply</c> would reject a bad result and so could never reveal one. This is the
	/// randomised cousin of <c>EntitySpec.Exhaustive</c>, and because starts are AutoBogus values rather than
	/// <c>New()</c> it also reaches states the factory never produces.
	/// </summary>
	public static void Preserves<T>(
		this ConstraintSet<T> constraints,
		EntityActions<T> actions,
		Gen<T>? start = null,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null
	)
	{
		if (actions.Count == 0)
		{
			throw new InvalidOperationException(
				$"{typeof(T).Name} has no actions to check; [Constraints] generates them from public methods that return {typeof(T).Name}."
			);
		}
		var starts = start ?? GenAuto.Of<T>().Valid(constraints, time);
		var runs = Gen.OneOf([
			.. actions.Select(a => Gen.Select(starts, AutoDefaults.ArgumentGen(a.ArgumentType), (s, x) => (Start: s, Action: a, Argument: x)))
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
			iter
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
		Sample(gen, samples, value =>
		{
			foreach (var v in constraints.Validate(value, time))
			{
				if (v.ConstraintId is { } id && violated.TryGetValue(id, out var count))
				{
					violated[id] = count + 1;
				}
			}
		});
		var report = new ProbeReport(samples, [.. names.Select(n => new ProbeEntry(n, samples - violated[n], violated[n]))]);
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
		var current = AutoDefaults.ValidStarts(constraints, time, 1)[0];
		var checkedCount = names.ToDictionary(n => n, _ => 0);
		var failures = new List<string>();
		var seenAll = false;
		Sample(raw, attempts, value =>
		{
			if (seenAll || EqualityComparer<T>.Default.Equals(value, current))
			{
				return;
			}
			var validate = constraints.Validate(value, time);
			foreach (var name in names.Where(n => validate.Any(v => v.ConstraintId == n) && checkedCount[n] < 3))
			{
				checkedCount[name]++;
				var apply = constraints.Apply(current, _ => value, time);
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

	private static List<string> PointInTimeNames<T>(ConstraintSet<T> constraints) =>
		[.. constraints.AsConstraints().Where(c => c.Kind == ConstraintKind.PointInTime).Select((c, i) => c.Name ?? $"constraint{i}")];

	private static void Sample<T>(Gen<T>? gen, int count, Action<T> each)
	{
		var source = gen ?? GenAuto.Of<T>();
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

	internal static IReadOnlyList<Violation> ViolationsOf<T>(ConstraintSet<T> constraints, T before, T after, TimeProvider? time)
	{
		time ??= TimeProvider.System;
		var unchanged = EqualityComparer<T>.Default.Equals(before, after);
		return [
			.. constraints
				.AsConstraints()
				.Where(c => c.Kind == ConstraintKind.PointInTime || !unchanged)
				.SelectMany(c => c.CheckAt(before, after, time).Select(v => v with { ConstraintId = c.Name })),
		];
	}
}

public sealed record ProbeEntry(string Constraint, int Held, int Violated);

public sealed class ProbeReport(int samples, IReadOnlyList<ProbeEntry> entries)
{
	public int Samples { get; } = samples;

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
