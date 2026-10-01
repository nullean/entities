// Nullean.Entities.CsCheck — vacuity checking for SampleWith.
//
// A constraint such as "never ship without confirming" is only evidence if the generated runs
// actually shipped something. If the generator never reaches Shipped, every run passes and the
// property proves nothing. Coverage<TEvent> names the events that must show up in the sample;
// SampleWith counts how many runs saw each one and throws VacuousPropertyException when one is
// missing — a failing test instead of a quietly green one.

using System.Globalization;
using System.Text;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// Events a sample must actually produce for passing to mean something. Pass one to
/// <c>SampleWith</c>; after the sample finishes, any <see cref="Expect"/>ed event seen in too few
/// runs fails with <see cref="VacuousPropertyException"/>.
/// </summary>
public sealed class Coverage<TEvent>
{
	private readonly List<(string Name, Func<TEvent, bool> Matches, double MinPercent)> _expectations = [];

	internal IReadOnlyList<(string Name, Func<TEvent, bool> Matches, double MinPercent)> Expectations => _expectations;

	/// <summary>
	/// Requires that <paramref name="matches"/> is true for at least one event in at least
	/// <paramref name="minPercentOfRuns"/> percent of runs. The default (0) means "at least one run
	/// in the whole sample" — the weakest useful bar, and the one that catches a generator that can
	/// never reach the case at all.
	/// </summary>
	public Coverage<TEvent> Expect(string name, Func<TEvent, bool> matches, double minPercentOfRuns = 0)
	{
		_expectations.Add((name, matches, minPercentOfRuns));
		return this;
	}
}

/// <summary>
/// A sample passed, but an expected event (see <see cref="Coverage{TEvent}"/>) was never — or too
/// rarely — produced, so the constraints under test were not exercised.
/// </summary>
public sealed class VacuousPropertyException(string message) : Exception(message);

internal sealed class CoverageTally<TEvent>(Coverage<TEvent> coverage)
{
	private readonly long[] _runsSeen = new long[coverage.Expectations.Count];
	private long _runs;

	public bool[] NewRun() => new bool[_runsSeen.Length];

	public void Observe(bool[] seen, TEvent e)
	{
		for (var i = 0; i < seen.Length; i++)
		{
			if (!seen[i] && coverage.Expectations[i].Matches(e))
			{
				seen[i] = true;
			}
		}
	}

	public void Complete(bool[] seen)
	{
		Interlocked.Increment(ref _runs);
		for (var i = 0; i < seen.Length; i++)
		{
			if (seen[i])
			{
				Interlocked.Increment(ref _runsSeen[i]);
			}
		}
	}

	/// <summary>Writes the coverage table and throws if any expectation was not met.</summary>
	public void Verify(Action<string>? writeLine)
	{
		var runs = Interlocked.Read(ref _runs);
		var table = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"Coverage over {runs} runs:");
		var failures = new List<string>();
		for (var i = 0; i < _runsSeen.Length; i++)
		{
			var (name, _, minPercent) = coverage.Expectations[i];
			var seen = Interlocked.Read(ref _runsSeen[i]);
			var percent = runs == 0 ? 0 : 100.0 * seen / runs;
			table.AppendLine(CultureInfo.InvariantCulture, $"  {name,-30} {seen,7} runs ({percent:F1}%)");
			if (seen == 0 || percent < minPercent)
			{
				failures.Add(seen == 0 ? $"\"{name}\" never occurred" : $"\"{name}\" occurred in {percent:F1}% of runs, expected at least {minPercent:F1}%");
			}
		}
		writeLine?.Invoke(table.ToString());
		if (failures.Count > 0)
		{
			throw new VacuousPropertyException(
				$"The sample passed vacuously: {string.Join("; ", failures)}.{Environment.NewLine}{table}" +
				"Widen the script (more steps, a longer Length, different Delays) or fix the generator so these events are reachable."
			);
		}
	}
}
