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
	) => new(rules, script, factory, log);

	/// <summary>The same for a service that exposes its events as an <see cref="IAsyncEnumerable{T}"/>. The stream is drained after each run, so use a service whose stream ends.</summary>
	public static EventAutoTest<TSut, TEvent> ForAsync<TSut, TEvent>(
		TemporalRuleSet<TEvent> rules,
		Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IAsyncEnumerable<Timestamped<TEvent>>> events
	) => new(rules, script, factory, sut => Drain(events(sut)));

	private static List<Timestamped<TEvent>> Drain<TEvent>(IAsyncEnumerable<Timestamped<TEvent>> stream)
	{
		var drained = new List<Timestamped<TEvent>>();
		var enumerator = stream.GetAsyncEnumerator();
		try
		{
			while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
			{
				drained.Add(enumerator.Current);
			}
		}
		finally
		{
			enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
		return drained;
	}
}

/// <summary>The event axes over one service. Immutable: each configuration method returns a copy.</summary>
public sealed class EventAutoTest<TSut, TEvent>
{
	private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(1);

	private readonly TemporalRuleSet<TEvent> _rules;
	private readonly Script<TSut> _script;
	private readonly Func<TimeProvider, TSut> _factory;
	private readonly Func<TSut, IEnumerable<Timestamped<TEvent>>> _log;
	private readonly int _samples = 200;
	private readonly string? _seed;
	private readonly Action<string>? _output;

	internal EventAutoTest(
		TemporalRuleSet<TEvent> rules,
		Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, IEnumerable<Timestamped<TEvent>>> log
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
	public AxisResult Absence() => Attack(Axis.Absence);

	/// <summary>Bounded responses (<c>After().Then().Within()</c>, <c>AfterUntil</c>) are answered in time: the delays tried straddle every window, and a rule fails as vacuous if no run ever crossed its deadline.</summary>
	public AxisResult Temporal() => Attack(Axis.Temporal);

	/// <summary>Everything a <c>from</c> event opens is eventually answered, once the run settles.</summary>
	public AxisResult Liveness() => Attack(Axis.Liveness);

	/// <summary>Every event rule fired in the sample: its trigger was seen, and a deadline was crossed. A rule that never fires reports green while checking nothing.</summary>
	public AxisResult Vacancy() => Attack(null);

	public AutoTestReport Report() => new([.. Axes.Select(Attempt), Attempt(Axis.Vacancy)], []);

	/// <summary>Runs every event axis and throws one exception listing the ones that failed.</summary>
	public AutoTestReport Run()
	{
		var report = Report();
		_output?.Invoke(report.ToString());
		return report.ThrowIfFailed();
	}

	/// <summary>One case per event axis that has a rule, plus vacancy.</summary>
	public IReadOnlyList<AxisCase> Cases =>
		[
			.. Axes.Concat([Axis.Vacancy])
				.Where(a => a == Axis.Vacancy || _rules.Rules.Any(r => r.Rule.Axis == a))
				.Select(a => new AxisCase(a, AxisInfo.Law(a), AxisInfo.Consequence(a), () => Case(a)))
		];

	private static Axis[] Axes { get; } = [Axis.Absence, Axis.Temporal, Axis.Liveness];

	private AxisResult Execute(Axis axis) => axis switch
	{
		Axis.Absence => Absence(),
		Axis.Temporal => Temporal(),
		Axis.Liveness => Liveness(),
		_ => Vacancy()
	};

	private AxisResult Attempt(Axis axis)
	{
		try
		{
			return Execute(axis);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return new AxisResult.Failed(axis, e.Message);
		}
	}

	private AxisResult Case(Axis axis)
	{
		var result = Attempt(axis);
		return result is AxisResult.Failed failed
			? throw new InvalidOperationException($"{axis} failed.\nLaw: {AxisInfo.Law(axis)}\nConsequence: {AxisInfo.Consequence(axis)}\n{failed.Report}")
			: result;
	}

	private AxisResult Attack(Axis? axis)
	{
		var picked = _rules.Rules.Where(r => axis is null ? r.Rule.Axis != Axis.Unclassified : r.Rule.Axis == axis).ToList();
		if (picked.Count == 0)
		{
			return axis is { } a ? new AxisResult.NotApplicable(a) : new AxisResult.NotApplicable(Axis.Vacancy);
		}
		var tally = new RuleTally(picked.Count);
		var set = TemporalRuleSet<TEvent>.From([.. picked]);
		var seed = _seed ?? AxisRun.DefaultSeed;
		BuildTimedGen(picked).Sample(run => Check(run, set, picked, tally), seed: seed, iter: _samples, print: run => run.Print());
		var vacuous = Vacuous(picked, tally);
		return vacuous.Count > 0
			? throw new VacuousPropertyException($"{axis ?? Axis.Vacancy} passed vacuously:\n{string.Join("\n", vacuous)}")
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

	private void Check(TimedRun<TSut> run, TemporalRuleSet<TEvent> set, List<(string Name, TemporalRule<TEvent> Rule)> picked, RuleTally tally)
	{
		run.Execute();
		var events = _log(run.Sut).ToList();
		var end = run.Time.GetUtcNow();
		tally.Add(picked, events, end);
		var report = Trace.Check(events, set, end);
		if (!report.Passed)
		{
			throw new InvalidOperationException(report.ToString());
		}
	}

	private static List<string> Vacuous(List<(string Name, TemporalRule<TEvent> Rule)> picked, RuleTally tally)
	{
		var found = new List<string>();
		for (var i = 0; i < picked.Count; i++)
		{
			var (name, rule) = picked[i];
			if (rule.Shape.Trigger is not null && tally.Triggered[i] == 0)
			{
				found.Add($"  {name}: no run contained an event that opens it");
			}
			else if (rule.Shape.Window is not null && tally.Crossed[i] == 0)
			{
				found.Add($"  {name}: no run went past the {rule.Shape.Window} deadline after a trigger, so it was never able to fail");
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
					if (shape.Trigger is not null && opening.Count > 0)
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
