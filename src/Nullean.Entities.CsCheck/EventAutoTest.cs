// Nullean.Entities.CsCheck — the event axes (absence, temporal, liveness) as named tests, driven by what each temporal rule declares.
//
// The state axes read a Constraint<T>; these read a TemporalRule<TEvent>. Each rule's EventShape says which events open it, answer it and how long
// it waits, so the test derives its own inputs: the delays tried are 0 and just before / just after every window, and a run only counts toward a rule
// if it actually contained the trigger (and, for a deadline, ran past it). Everything runs on a ManualTimeProvider with the core temporal rules; no Rx.

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>Starts an <see cref="EventAutoTest{TSut, TEvent}"/>.</summary>
public static class EventAutoTest
{
	/// <summary>
	/// An auto-test over <paramref name="rules"/>: <paramref name="script"/> gives the operations, <paramref name="factory"/> builds the service around the run's
	/// <see cref="TimeProvider"/>, <paramref name="log"/> reads back the events it recorded. A script that sets no delays gets ones derived from the rules' windows.
	/// </summary>
	public static EventAutoTest<TSut, TEvent> For<TSut, TEvent>(
		TemporalRuleSet<TEvent> rules,
		Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IEnumerable<Timestamped<TEvent>>> log
	) => new(rules, script, factory, sut => ValueTask.FromResult(log(sut).ToList()));

	/// <summary>The same for a service that exposes its events as an <see cref="IAsyncEnumerable{T}"/>. The stream is drained after each run, so use a service whose stream ends.</summary>
	public static EventAutoTest<TSut, TEvent> ForAsync<TSut, TEvent>(
		TemporalRuleSet<TEvent> rules,
		Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IAsyncEnumerable<Timestamped<TEvent>>> events
	) =>
		new(rules, script, factory, async sut =>
		{
			var drained = new List<Timestamped<TEvent>>();
			await foreach (var e in events(sut).ConfigureAwait(false))
			{
				drained.Add(e);
			}
			return drained;
		});
}

/// <summary>The event axes over one service. Immutable: each configuration method returns a copy.</summary>
public sealed class EventAutoTest<TSut, TEvent>
{
	private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(1);

	private readonly TemporalRuleSet<TEvent> _rules;
	private readonly Script<TSut> _script;
	private readonly Func<TimeProvider, TSut> _factory;
	private readonly Func<TSut, ValueTask<List<Timestamped<TEvent>>>> _log;
	private readonly int _samples = 200;
	private readonly string? _seed;
	private readonly Action<string>? _output;

	internal EventAutoTest(
		TemporalRuleSet<TEvent> rules,
		Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, ValueTask<List<Timestamped<TEvent>>>> log
	)
	{
		_rules = rules;
		_script = script;
		_factory = factory;
		_log = log;
	}

	private EventAutoTest(EventAutoTest<TSut, TEvent> from, int samples, string? seed, Action<string>? output) : this(
			from._rules,
			from._script,
			from._factory,
			from._log
		)
	{
		_samples = samples;
		_seed = seed;
		_output = output;
	}

	public EventAutoTest<TSut, TEvent> Samples(int samples) => new(this, samples, _seed, _output);

	public EventAutoTest<TSut, TEvent> Seed(string seed) => new(this, _samples, seed, _output);

	public EventAutoTest<TSut, TEvent> Output(Action<string> writeLine) => new(this, _samples, _seed, writeLine);

	/// <summary>Sequences the domain forbids (<c>Never</c>, <c>NeverTwice</c>, <c>Always</c>, <c>Transitions</c>, <c>When</c>) never happen, whatever the calls and delays.</summary>
	public Task<AxisResult> AbsenceAsync() => AttackAsync(Axis.Absence);

	/// <summary>Bounded responses (<c>After().Then().Within()</c>, <c>AfterUntil</c>) are answered in time: the delays tried straddle every window, and a rule fails as vacuous if no run ever crossed its deadline.</summary>
	public Task<AxisResult> TemporalAsync() => AttackAsync(Axis.Temporal);

	/// <summary>Everything a <c>from</c> event opens is eventually answered, once the run settles.</summary>
	public Task<AxisResult> LivenessAsync() => AttackAsync(Axis.Liveness);

	/// <summary>Every event rule fired in the sample: its trigger was seen, and a deadline was crossed. A rule that never fires reports green while checking nothing.</summary>
	public Task<AxisResult> VacancyAsync() => AttackAsync(null);

	public async Task<AutoTestReport> ReportAsync()
	{
		var results = new List<AxisResult>();
		foreach (var axis in Axes)
		{
			results.Add(await AttemptAsync(axis).ConfigureAwait(false));
		}
		results.Add(await AttemptAsync(Axis.Vacancy).ConfigureAwait(false));
		return new AutoTestReport(results, [.. Unclassified.Select(r => r.Name)]);
	}

	/// <summary>Runs every event axis and throws one exception listing the ones that failed.</summary>
	public async Task<AutoTestReport> RunAsync()
	{
		var report = await ReportAsync().ConfigureAwait(false);
		_output?.Invoke(report.ToString());
		return report.ThrowIfFailed();
	}

	/// <summary>One case per event axis that has a rule, plus vacancy.</summary>
	public IReadOnlyList<AxisCase> Cases =>
		[
			.. Axes.Concat([Axis.Vacancy])
				.Where(a => a == Axis.Vacancy || _rules.Rules.Any(r => r.Rule.Axis == a))
				.Select(a => new AxisCase(a, AxisInfo.Law(a), AxisInfo.Consequence(a), () => CaseAsync(a)))
		];

	private IEnumerable<(string Name, TemporalRule<TEvent> Rule)> Unclassified => _rules.Rules.Where(r => r.Rule.Axis == Axis.Unclassified);

	private static Axis[] Axes { get; } = [Axis.Absence, Axis.Temporal, Axis.Liveness];

	private Task<AxisResult> ExecuteAsync(Axis axis) => axis switch
	{
		Axis.Absence => AbsenceAsync(),
		Axis.Temporal => TemporalAsync(),
		Axis.Liveness => LivenessAsync(),
		_ => VacancyAsync()
	};

	private async Task<AxisResult> AttemptAsync(Axis axis)
	{
		try
		{
			return await ExecuteAsync(axis).ConfigureAwait(false);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return new AxisResult.Failed(axis, e.Message);
		}
	}

	private async Task<AxisResult> CaseAsync(Axis axis)
	{
		var result = await AttemptAsync(axis).ConfigureAwait(false);
		return result is AxisResult.Failed failed
			? throw new InvalidOperationException(
				failed.IsFormatted
					? failed.Report
					: $"{axis} failed.\n\nProblem      {failed.Report}\n\nLaw          {AxisInfo.Law(axis)}\nConsequence  {AxisInfo.Consequence(axis)}"
			)
			: result;
	}

	private Task<AxisResult> AttackAsync(Axis? axis) => AxisReport.GuardAsync(axis ?? Axis.Vacancy, () => AttackCoreAsync(axis));

	private async Task<AxisResult> AttackCoreAsync(Axis? axis)
	{
		var picked = _rules.Rules.Where(r => axis is null ? r.Rule.Axis != Axis.Unclassified : r.Rule.Axis == axis).ToList();
		if (picked.Count == 0)
		{
			return axis is { } a ? new AxisResult.NotApplicable(a) : new AxisResult.NotApplicable(Axis.Vacancy);
		}
		var tally = new RuleTally(picked.Count);
		// rules no axis can aim at are still checked for violations by the all-rules run, they just have no trigger to judge vacuity by
		var set = TemporalRuleSet<TEvent>.From([.. picked, .. axis is null ? Unclassified : []]);
		var seed = _seed ?? AxisRun.DefaultSeed;
		await BuildTimedGen(picked)
			.SampleAsync(run => CheckAsync(run, set, picked, tally), seed: seed, iter: _samples, print: run => run.Print())
			.ConfigureAwait(false);
		var vacuous = Vacuous(picked, tally);
		return vacuous.Count > 0
			? throw new AxisFailure(
				[
					.. vacuous.SelectMany(v => new[] { ("Rule", v.Rule), ("Not exercised because", v.Why) }),
					("Next", "give the script a step that makes the rule's trigger happen, and (for a deadline) let delays carry a run past it.")
				],
				vacuous: true
			)
			: new AxisResult.Passed(axis ?? Axis.Vacancy, [.. picked.Select(r => r.Name)], _samples);
	}

	private Gen<TimedRun<TSut>> BuildTimedGen(IReadOnlyList<(string Name, TemporalRule<TEvent> Rule)> picked) =>
		ScriptExtensions.BuildTimedGen(_script, _factory, _script.HasDelays ? null : DerivedDelays(picked));

	// Zero, and just either side of every window, so a deadline is both met and missed within a few draws.
	private static Gen<TimeSpan> DerivedDelays(IReadOnlyList<(string Name, TemporalRule<TEvent> Rule)> picked)
	{
		var delays = new List<TimeSpan> { TimeSpan.Zero };
		foreach (var window in picked.Select(r => r.Rule.Shape.Window).OfType<TimeSpan>())
		{
			delays.Add(window + Epsilon);
			delays.Add(window > Epsilon ? window - Epsilon : TimeSpan.Zero);
		}
		return Gen.OneOfConst([.. delays.Distinct()]);
	}

	private async Task CheckAsync(TimedRun<TSut> run, TemporalRuleSet<TEvent> set, List<(string Name, TemporalRule<TEvent> Rule)> picked, RuleTally tally)
	{
		await run.ExecuteAsync().ConfigureAwait(false);
		var events = await _log(run.Sut).ConfigureAwait(false);
		var end = run.Time.GetUtcNow();
		tally.Add(picked, events, end);
		var report = Trace.Check(events, set, end);
		if (!report.Passed)
		{
			var first = events.Count > 0 ? events[0].At : end;
			throw new AxisFailure(
				[
					.. report.Violations.SelectMany(
						v =>
							new[]
							{
								("Rule broken", v.Name),
								("Noticed at", v.Verdict.ViolationAt is { } at ? $"+{(at - first).TotalSeconds:0.###}s" : ""),
								("What happened", Describe(v.Verdict.ViolationEvidence))
							}
					),
					("Events recorded", events.Count == 0
						? "none"
						: string.Join(
							"\n",
							events.Select(
								e => $"+{(e.At - first).TotalSeconds:0.###}s  {ValuePrinter.Print(e.Event).Replace(" {  }", "", StringComparison.Ordinal)}"
							)
						))
				],
				printedCaseLabel: "Calls made"
			);
		}
	}

	// "(Cancelled { }, Shipped { })" reads as "Cancelled, then Shipped"; an empty record prints as its name
	private static string Describe(object? evidence) =>
		evidence is System.Runtime.CompilerServices.ITuple pair
			? string.Join(", then ", Enumerable.Range(0, pair.Length).Select(i => Clean(pair[i])))
			: Clean(evidence);

	private static string Clean(object? value) =>
		(value is string text ? text : ValuePrinter.Print(value)).Replace(" {  }", "", StringComparison.Ordinal).Replace(" { }", "", StringComparison.Ordinal);

	private static List<(string Rule, string Why)> Vacuous(List<(string Name, TemporalRule<TEvent> Rule)> picked, RuleTally tally)
	{
		var found = new List<(string Rule, string Why)>();
		for (var i = 0; i < picked.Count; i++)
		{
			var (name, rule) = picked[i];
			if (tally.Triggered[i] == 0)
			{
				found.Add((name, rule.Shape.Trigger is null ? "no run recorded any event" : "no run contained an event that opens it"));
			}
			else if (rule.Shape.Window is not null && tally.Crossed[i] == 0)
			{
				found.Add((name, $"no run went past the {rule.Shape.Window} deadline after a trigger, so it was never able to fail"));
			}
		}
		return found;
	}

	private sealed class RuleTally(int count)
	{
		private readonly Lock _gate = new();

		public int[] Triggered { get; } = new int[count];

		public int[] Crossed { get; } = new int[count];

		public void Add(List<(string Name, TemporalRule<TEvent> Rule)> picked, List<Timestamped<TEvent>> events, DateTimeOffset end)
		{
			lock (_gate)
			{
				for (var i = 0; i < picked.Count; i++)
				{
					var shape = picked[i].Rule.Shape;
					var opening = shape.Trigger is null ? events : [.. events.Where(e => shape.Trigger(e.Event))];
					if (opening.Count > 0)
					{
						Triggered[i]++;
					}
					if (shape.Window is { } window && opening.Any(e => e.At + window <= end))
					{
						Crossed[i]++;
					}
				}
			}
		}
	}
}
