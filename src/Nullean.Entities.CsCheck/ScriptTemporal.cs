// Nullean.Entities.CsCheck — Script<TSut> on a TimeProvider, checked with the core temporal rules (no Rx).
//
// This is the default way to run a script: it drives a ManualTimeProvider (the Rx scheduler variant lives in the opt-in
// Nullean.Entities.CsCheck.Reactive package): the SUT takes a TimeProvider, records timestamped events (an in-memory log), and the generated
// delays advance that clock. The run is then checked with Trace.Check, so a service with no Rx dependency gets the same
// generated delays, shrinking and vacuity checks.

using CsCheck;

namespace Nullean.Entities.CsCheck;

internal sealed class TimedRun<TSut>(TSut sut, ManualTimeProvider time, (TimeSpan Delay, ScriptStep<TSut> Step)[] ops)
{
	public TSut Sut { get; } = sut;

	public ManualTimeProvider Time { get; } = time;

	public (TimeSpan Delay, ScriptStep<TSut> Step)[] Ops { get; } = ops;

	public async Task ExecuteAsync()
	{
		foreach (var (delay, step) in Ops)
		{
			if (delay > TimeSpan.Zero)
			{
				Time.Advance(delay);
			}
			await step.RunAsync(Sut).ConfigureAwait(false);
		}
	}

	public string Print() =>
		Ops.Length == 0
			? "(no steps)"
			: string.Join(" -> ", Ops.Select(o => o.Delay > TimeSpan.Zero ? $"wait {o.Delay.TotalSeconds:0.###}s, {o.Step.Name}" : o.Step.Name));
}

public static partial class ScriptExtensions
{
	internal static Gen<TimedRun<TSut>> BuildTimedGen<TSut>(Script<TSut> script, Func<TimeProvider, TSut> factory, Gen<TimeSpan>? delays = null)
	{
		if (script.Steps.Count == 0)
		{
			throw new InvalidOperationException("Script<TSut> has no steps — call Step(...) at least once before SampleWith.");
		}
		var stepGen = Gen.OneOf(script.Steps.ToArray());
		var opGen = Gen.Select(delays ?? script.Delay, stepGen, (delay, step) => (delay, step));
		return opGen
			.Array[script.MinLength, script.MaxLength]
			.Select(ops =>
			{
				var time = new ManualTimeProvider();
				return new TimedRun<TSut>(factory(time), time, ops);
			});
	}

	private static async Task CheckTimedRunAsync<TSut, TEvent>(
		TimedRun<TSut> run,
		Func<TSut, IEnumerable<Timestamped<TEvent>>> log,
		TemporalRuleSet<TEvent> rules,
		CoverageTally<TEvent>? tally
	)
	{
		await run.ExecuteAsync().ConfigureAwait(false);
		var events = log(run.Sut).ToList();
		if (tally is not null)
		{
			var seen = tally.NewRun();
			foreach (var stamped in events)
			{
				tally.Observe(seen, stamped.Event);
			}
			tally.Complete(seen);
		}
		// Delays apply before each step, never after the last: an obligation still open when the run ends stays pending.
		var report = Trace.Check(events, rules, run.Time.GetUtcNow());
		if (!report.Passed)
		{
			throw new InvalidOperationException(report.ToString());
		}
	}

	/// <summary>
	/// <c>SampleWith</c> without Rx: <paramref name="factory"/> builds the SUT around the run's <see cref="TimeProvider"/> (a
	/// <see cref="ManualTimeProvider"/> that the script's generated delays advance), <paramref name="log"/> reads back the timestamped
	/// events it recorded, and <paramref name="rules"/> (core temporal rules) are checked against them. A violation fails with the rule,
	/// its time and evidence, a shrunk walk and a seed.
	/// </summary>
	public static Task SampleWithAsync<TSut, TEvent>(
		this Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IEnumerable<Timestamped<TEvent>>> log,
		TemporalRuleSet<TEvent> rules,
		long iter = -1,
		string? seed = null
	) => BuildTimedGen(script, factory).SampleAsync(run => CheckTimedRunAsync(run, log, rules, null), seed: seed, iter: iter, print: run => run.Print());

	/// <summary>
	/// The same, refusing to pass vacuously: every <see cref="Coverage{TEvent}.Expect"/>ed event must have been recorded in enough runs, otherwise
	/// <see cref="VacuousPropertyException"/>. Skipped when <paramref name="seed"/> is given (one replayed run is too small a sample).
	/// </summary>
	public static async Task SampleWithAsync<TSut, TEvent>(
		this Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IEnumerable<Timestamped<TEvent>>> log,
		TemporalRuleSet<TEvent> rules,
		Coverage<TEvent> coverage,
		long iter = -1,
		string? seed = null,
		Action<string>? writeLine = null
	)
	{
		var tally = new CoverageTally<TEvent>(coverage);
		await BuildTimedGen(script, factory)
			.SampleAsync(run => CheckTimedRunAsync(run, log, rules, tally), seed: seed, iter: iter, print: run => run.Print())
			.ConfigureAwait(false);
		if (seed is null)
		{
			tally.Verify(writeLine);
		}
	}
}
