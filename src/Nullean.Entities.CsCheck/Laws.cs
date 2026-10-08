// Nullean.Entities.CsCheck — Laws(): the checks every constraint set should pass, in one line.
//
//   1. a valid value breaks nothing,
//   2. every constraint has a name, and names are unique (a violation says which rule it is),
//   3. every constraint has a counter-example (a value that breaks it): otherwise nothing shows the rule can ever fire,
//   4. each counter-example breaks only its own constraint, so a failing test names one cause.
//
// Counter-examples come from you (a function that makes the valid value wrong in the way the rule forbids) or, for point-in-time rules over a
// type the generator can read, from the edge-value swaps behind Boundaries(): a single property set to a value that this rule alone rejects.

using System.Globalization;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

public sealed record LawResult(string Constraint, string CounterExample);

public sealed class LawsReport(IReadOnlyList<LawResult> results)
{
	public IReadOnlyList<LawResult> Results { get; } = results;

	public override string ToString()
	{
		var sb = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"{Results.Count} constraint(s) have a counter-example that breaks only them:");
		foreach (var result in Results)
		{
			sb.AppendLine(CultureInfo.InvariantCulture, $"  {result.Constraint}: {result.CounterExample}");
		}
		return sb.ToString();
	}
}

public static class ConstraintLaws
{
	/// <summary>
	/// Asserts the four laws above for <paramref name="constraints"/>, starting from <paramref name="valid"/>. <paramref name="counterExamples"/> maps a
	/// constraint name to a function that turns the valid value into one that breaks that constraint (and only it); any constraint without one gets
	/// one generated where a single-property edge value rejected by that rule alone exists. Throws with every failure listed, so a broken set is
	/// diagnosed in one run. With <paramref name="requireCounterExamples"/> off, a constraint with no counter-example is reported in
	/// the result instead of failing (cross-property rules often need one written by hand).
	/// </summary>
	public static LawsReport Laws<T>(
		this ConstraintSet<T> constraints,
		T valid,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		bool requireCounterExamples = true,
		TimeProvider? time = null,
		Action<string>? writeLine = null,
		Action<AutoOptions>? options = null
	)
	{
		var all = constraints.AsConstraints();
		var failures = new List<string>();
		var results = new List<LawResult>();

		// 1. valid breaks nothing
		var broken = ConstraintProperties.SafeValidate(constraints, valid, time).Select(v => $"{v.ConstraintId}: {v.Message}").ToList();
		if (broken.Count > 0)
		{
			failures.Add($"The valid value is not valid ({ConstraintProperties.SafePrint(valid)}): {string.Join("; ", broken)}");
		}

		// 2. named, and unique
		for (var i = 0; i < all.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(all[i].Name))
			{
				failures.Add($"Constraint #{i + 1} has no name, so a violation cannot say which rule it is.");
			}
		}
		foreach (var duplicate in all.Where(c => c.Name is not null).GroupBy(c => c.Name!).Where(g => g.Count() > 1))
		{
			failures.Add($"The name '{duplicate.Key}' is used by {duplicate.Count()} constraints.");
		}
		var known = all.Select(c => c.Name).Where(n => n is not null).ToHashSet();
		foreach (var unknown in (counterExamples?.Keys ?? []).Where(k => !known.Contains(k)))
		{
			failures.Add($"A counter-example is given for '{unknown}', which is not a constraint in the set.");
		}

		// 3 and 4. a counter-example each, breaking only its own constraint
		List<ConstraintBoundaries.Swap<T>>? swaps = null;
		foreach (var constraint in all.Where(c => c.Name is not null))
		{
			var name = constraint.Name!;
			T? broken1 = default;
			string? how = null;
			if (counterExamples is not null && counterExamples.TryGetValue(name, out var make))
			{
				try
				{
					broken1 = make(valid);
					how = "given";
				}
				catch (Exception e) when (e is not OutOfMemoryException)
				{
					failures.Add($"{name}: the counter-example threw {e.GetType().Name}: {e.Message}");
					continue;
				}
			}
			else if (constraint.Kind == ConstraintKind.PointInTime)
			{
				swaps ??= Swaps(constraints, valid, time, options);
				var swap = swaps.FirstOrDefault(s => s.Instance is not null && s.RejectedBy.Count == 1 && s.RejectedBy[0] == name);
				if (swap is not null)
				{
					broken1 = swap.Instance;
					how = $"generated: {swap.Property} = {swap.Value}";
				}
			}
			if (how is null)
			{
				if (requireCounterExamples)
				{
					failures.Add(
						$"{name}: no counter-example. Give one in counterExamples, or no single-property edge value is rejected by this rule alone (it may overlap another rule, or span several properties)."
					);
				}
				continue;
			}

			List<string> ids;
			try
			{
				ids = [.. constraints.Check(valid, broken1!, time).Select(v => v.ConstraintId ?? "?").Distinct()];
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				failures.Add($"{name}: checking its counter-example threw {e.GetType().Name}: {e.Message}");
				continue;
			}
			if (!ids.Contains(name))
			{
				failures.Add($"{name}: its counter-example ({how}) does not break it.");
			}
			else if (ids.Count > 1)
			{
				failures.Add(
					$"{name}: its counter-example ({how}) also breaks {string.Join(", ", ids.Where(i => i != name))}, so a failure would not point at one cause."
				);
			}
			else
			{
				results.Add(new LawResult(name, how));
			}
		}

		var report = new LawsReport(results);
		writeLine?.Invoke(report.ToString());
		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"The constraint set breaks {failures.Count} law(s):{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", failures)}"
			);
		}
		return report;
	}

	/// <summary>
	/// The same laws over generated data instead of one hand-written value: <paramref name="valid"/> is a generator (usually
	/// <c>GenAuto.Of&lt;T&gt;(AutoMode.Plausible, configure)</c> with the relations that make its values valid, or any <c>Gen&lt;T&gt;</c>) and
	/// <paramref name="samples"/> values are drawn from a fixed <paramref name="seed"/> (so a failure replays). Asserts that
	///   1. every generated value breaks no rule (a failure is a disagreement between the model and its rules, listed with the value),
	///   2. every rule has a name, and the names are unique,
	///   3. every rule is reachable: its counter-example, applied to the generated values, breaks it (otherwise nothing shows the rule can fire),
	///   4. a counter-example derived from any generated value breaks only its own rule.
	/// A counter-example that changes nothing for some generated value (it removes an item from an empty list, say) is skipped for that value; it must
	/// break its rule for at least one of the first <paramref name="derivedFrom"/> values. A rule without a counter-example gets one from the edge-value
	/// swaps of the first generated value, where a single property edge rejected by that rule alone exists.
	/// </summary>
	public static LawsReport Laws<T>(
		this ConstraintSet<T> constraints,
		Gen<T> valid,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		int samples = 300,
		int derivedFrom = 25,
		uint seed = 20_260_101,
		bool requireCounterExamples = true,
		TimeProvider? time = null,
		Action<string>? writeLine = null,
		Action<AutoOptions>? options = null
	)
	{
		var all = constraints.AsConstraints();
		var failures = new List<string>();
		var results = new List<LawResult>();

		// 1. generated values break nothing
		var values = new List<T>(samples);
		var brokenBy = new Dictionary<string, int>();
		var examples = new List<string>();
		for (var i = 0; i < samples; i++)
		{
			T value;
			try
			{
				value = valid.Generate(new PCG(seed, (ulong)i + 1), null, out _);
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				failures.Add($"The generator threw on sample {i + 1} of {samples}: {e.GetType().Name}: {e.Message}");
				break;
			}
			values.Add(value);
			var broken = ConstraintProperties.SafeValidate(constraints, value, time);
			if (broken.Count == 0)
			{
				continue;
			}
			foreach (var id in broken.Select(v => v.ConstraintId ?? "?").Distinct())
			{
				brokenBy[id] = brokenBy.GetValueOrDefault(id) + 1;
			}
			if (examples.Count < 3)
			{
				examples.Add($"{ValuePrinter.Print(value)} breaks {string.Join("; ", broken.Select(v => $"{v.ConstraintId}: {v.Message}"))}");
			}
		}
		if (brokenBy.Count > 0)
		{
			failures.Add(
				$"Generated values are not valid: {string.Join(", ", brokenBy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} ({kv.Value} of {values.Count})"))}. First: {string.Join(" | ", examples)}"
			);
		}

		// 2. named, and unique
		for (var i = 0; i < all.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(all[i].Name))
			{
				failures.Add($"Constraint #{i + 1} has no name, so a violation cannot say which rule it is.");
			}
		}
		foreach (var duplicate in all.Where(c => c.Name is not null).GroupBy(c => c.Name!).Where(g => g.Count() > 1))
		{
			failures.Add($"The name '{duplicate.Key}' is used by {duplicate.Count()} constraints.");
		}
		var known = all.Select(c => c.Name).Where(n => n is not null).ToHashSet();
		foreach (var unknown in (counterExamples?.Keys ?? []).Where(k => !known.Contains(k)))
		{
			failures.Add($"A counter-example is given for '{unknown}', which is not a constraint in the set.");
		}

		// 3 and 4. every rule reachable from a counter-example; the counter-example breaks only its own rule
		var bases = values.Take(derivedFrom).ToList();
		List<ConstraintBoundaries.Swap<T>>? swaps = null;
		foreach (var constraint in all.Where(c => c.Name is not null))
		{
			var name = constraint.Name!;
			var attempts = new List<(T Base, T Broken, string How)>();
			if (counterExamples is not null && counterExamples.TryGetValue(name, out var make))
			{
				try
				{
					attempts.AddRange(bases.Select(b => (b, make(b), "given")));
				}
				catch (Exception e) when (e is not OutOfMemoryException)
				{
					failures.Add($"{name}: the counter-example threw {e.GetType().Name}: {e.Message}");
					continue;
				}
			}
			else if (constraint.Kind == ConstraintKind.PointInTime && bases.Count > 0)
			{
				swaps ??= Swaps(constraints, bases[0], time, options);
				var swap = swaps.FirstOrDefault(s => s.Instance is not null && s.RejectedBy.Count == 1 && s.RejectedBy[0] == name);
				if (swap is not null)
				{
					attempts.Add((bases[0], swap.Instance!, $"generated: {swap.Property} = {swap.Value}"));
				}
			}
			if (attempts.Count == 0)
			{
				if (requireCounterExamples)
				{
					failures.Add(
						$"{name}: no counter-example. Give one in counterExamples, or no single-property edge value is rejected by this rule alone (it may overlap another rule, or span several properties)."
					);
				}
				continue;
			}

			var reached = 0;
			string? firstIssue = null;
			foreach (var (baseValue, broken, how) in attempts)
			{
				List<string> ids;
				try
				{
					ids = [.. constraints.Check(baseValue, broken, time).Select(v => v.ConstraintId ?? "?").Distinct()];
				}
				catch (Exception e) when (e is not OutOfMemoryException)
				{
					firstIssue ??= $"{name}: checking its counter-example threw {e.GetType().Name}: {e.Message}";
					continue;
				}
				if (!ids.Contains(name))
				{
					continue;
				}
				if (ids.Count > 1)
				{
					firstIssue ??=
						$"{name}: its counter-example ({how}), derived from {ValuePrinter.Print(baseValue)}, also breaks {string.Join(", ", ids.Where(i => i != name))}, so a failure would not point at one cause.";
					continue;
				}
				reached++;
			}
			if (firstIssue is not null)
			{
				failures.Add(firstIssue);
			}
			else if (reached == 0)
			{
				failures.Add(
					$"{name}: its counter-example ({attempts[0].How}) breaks nothing for any of {attempts.Count} generated value(s), so the rule is unreachable from generated data."
				);
			}
			else
			{
				results.Add(new LawResult(name, $"{attempts[0].How}, broke it for {reached} of {attempts.Count} generated value(s)"));
			}
		}

		var report = new LawsReport(results);
		writeLine?.Invoke(report.ToString());
		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"The constraint set breaks {failures.Count} law(s) over {values.Count} generated value(s):{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", failures)}"
			);
		}
		return report;
	}

	private static List<ConstraintBoundaries.Swap<T>> Swaps<T>(ConstraintSet<T> constraints, T valid, TimeProvider? time, Action<AutoOptions>? options)
	{
		try
		{
			return [.. ConstraintBoundaries.Swaps(constraints, valid, time, options)];
		}
		catch (InvalidOperationException)
		{
			return [];
		}
	}
}
