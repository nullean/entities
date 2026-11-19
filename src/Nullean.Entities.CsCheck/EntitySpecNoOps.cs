// Nullean.Entities.CsCheck — EntitySpec<T>.NoOps(): "this action has no effect", asked of the requirements and the coverage together.
//
// Faults() injects "X has no effect" for every action, but only the requirements can catch it, and a spec whose lifecycle is an Allow list
// catches that fault through coverage instead: with X doing nothing, an Allow edge is never taken and Exhaustive() fails with "never exercised".
// NoOps() replaces each action in turn with one that returns its input and runs Exhaustive() on it, so both count.

using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>How a do-nothing replacement of one action was noticed, if it was.</summary>
public enum NoOpOutcome
{
	/// <summary>A requirement failed.</summary>
	CaughtByRequirement,

	/// <summary>Every requirement held, but a rule, <c>Allow</c> edge or action that was exercised with the real action was not.</summary>
	CaughtByCoverage,

	/// <summary>Nothing noticed: the action can do nothing and every requirement and every coverage check still passes.</summary>
	Undetected
}

/// <summary>What replacing one action by a no-op did. <paramref name="By"/> is the requirement that failed, or the rules and edges that were no longer exercised.</summary>
public sealed record NoOpResult(string Action, NoOpOutcome Outcome, string? By);

/// <summary>The result of <c>EntitySpec.NoOps()</c>: one entry per action.</summary>
public sealed record NoOpsReport(IReadOnlyList<NoOpResult> Results)
{
	/// <summary>The actions whose do-nothing replacement went unnoticed.</summary>
	public IReadOnlyList<string> Undetected => [.. Results.Where(r => r.Outcome == NoOpOutcome.Undetected).Select(r => r.Action)];

	public override string ToString()
	{
		var sb = new StringBuilder();
		foreach (var r in Results)
		{
			_ = sb.AppendLine(r.Outcome switch
			{
				NoOpOutcome.CaughtByRequirement => $"  {r.Action} has no effect: caught by requirement {r.By}",
				NoOpOutcome.CaughtByCoverage => $"  {r.Action} has no effect: caught by coverage, {r.By} never exercised",
				_ => $"  {r.Action} has no effect: NOT DETECTED"
			});
		}
		return sb.ToString();
	}
}

public sealed partial class EntitySpec<T>
{
	/// <summary>
	/// Replaces each action in turn with one that returns its input and asks whether anything notices: a requirement fails, or (the way
	/// <see cref="Exhaustive"/>'s <c>requireCoverage</c> does) a <c>When</c> guard, <c>Allow</c> edge or action that the real action exercised is no longer exercised. This is
	/// <see cref="Faults"/>' automatic "X has no effect" made a real check: it counts coverage, which <c>Faults</c> cannot, and throws for an action whose
	/// do-nothing replacement goes unnoticed (unless <paramref name="throwOnUndetected"/> is false). With several starts an action is detected when any start's
	/// requirements fail, or when a rule is unexercised from every start.
	/// </summary>
	public NoOpsReport NoOps(Action<string>? writeLine = null, int maxStates = 100_000, bool throwOnUndetected = true)
	{
		var starts = Starts();
		foreach (var start in starts)
		{
			CheckStart(start);
		}
		var baseline = Unexercised(starts, null, maxStates, out var baselineFailure);
		if (baselineFailure is not null)
		{
			throw new InvalidOperationException($"NoOps compares against the unbroken entity, and Exhaustive() fails on that first: {baselineFailure}");
		}

		var results = new List<NoOpResult>();
		foreach (var action in _actions.Select(a => a.Name).Distinct())
		{
			var unexercised = Unexercised(starts, action, maxStates, out var failure);
			if (failure is not null)
			{
				results.Add(new NoOpResult(action, NoOpOutcome.CaughtByRequirement, failure));
				continue;
			}
			// an action that changed nothing even before is not "exercised" either way: only what this replacement newly stops exercising counts
			var lost = unexercised.Except(baseline).Where(name => name != action).Order(StringComparer.Ordinal).ToList();
			results.Add(
				lost.Count > 0
					? new NoOpResult(action, NoOpOutcome.CaughtByCoverage, string.Join(", ", lost))
					: new NoOpResult(action, NoOpOutcome.Undetected, null)
			);
		}
		var report = new NoOpsReport(results);
		writeLine?.Invoke(report.ToString());
		if (throwOnUndetected && report.Undetected.Count > 0)
		{
			throw new CsCheckException(
				$"No requirement or coverage check notices when these actions do nothing: {string.Join(", ", report.Undetected)}. A rule that only holds because the action never ran is missing, or the action can legitimately change nothing from every start.{Environment.NewLine}{report}"
			);
		}
		return report;
	}

	// What no start exercised, with `noOp` (when given) replaced by a no-op; failure is the first requirement that failed from any start. The intersection over
	// starts is the one Exhaustive uses: a rule counts as exercised if any start exercised it.
	private HashSet<string> Unexercised(IReadOnlyList<T> starts, string? noOp, int maxStates, out string? failure)
	{
		failure = null;
		HashSet<string>? unexercised = null;
		foreach (var start in starts)
		{
			EntitySpecReport report;
			try
			{
				report = EntitySpecReport.From(SampleCall.Run(() => Build(start, autoFaults: false, noOp).Spec.Exhaustive(null, maxStates)));
			}
			catch (CsCheckException e)
			{
				failure = FirstLine(e.Message);
				return [];
			}
			var missed = report.NeverTriggered.Concat(report.NeverFired).ToHashSet();
			if (unexercised is null)
			{
				unexercised = missed;
			}
			else
			{
				unexercised.IntersectWith(missed);
			}
		}
		return unexercised ?? [];
	}

	private static string FirstLine(string message) =>
		message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(
			l => !l.StartsWith("Set seed", StringComparison.Ordinal)
		) ?? message;
}
