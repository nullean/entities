using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Reactive;
using TUnit;

// Same alias as ScriptTests.cs: the sync Constraints<T> is in unqualified scope ahead of the reactive one.
using Rx = Nullean.Entities.Reactive.Constraints<Nullean.Entities.CsCheck.Tests.Ev2>;

namespace Nullean.Entities.CsCheck.Tests;

internal abstract record Ev2
{
	public sealed record A(int Id) : Ev2;

	public sealed record B(int Id) : Ev2;

	public sealed record C(int Id) : Ev2;
}

// Nullean.Entities.Reactive and the temporal monitors in Nullean.Entities implement the same operators twice, over IObservable and
// over a pure state machine. They must agree: feed both the same generated, timestamped trace and compare the verdicts and
// the time of each violation. Drift between them is a test failure here.
public class TemporalDifferentialTests
{
	private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

	private static readonly (string Name, Func<ReactiveConstraint<Ev2>> Reactive, Func<TemporalRule<Ev2>> Core)[] Pairs =
	[
		("Always", () => Rx.Always<Ev2.A>(a => a.Id != 2), () => Temporal<Ev2>.Always<Ev2.A>(a => a.Id != 2)),
		("Never", () => Rx.Never<Ev2.A, Ev2.B>((a, b) => a.Id == b.Id), () => Temporal<Ev2>.Never<Ev2.A, Ev2.B>((a, b) => a.Id == b.Id)),
		("NeverTwice", () => Rx.NeverTwice<Ev2.A>(a => a.Id), () => Temporal<Ev2>.NeverTwice<Ev2.A>(a => a.Id)),
		("Transitions", () => Rx.Transitions((p, n) => !(p is Ev2.A && n is Ev2.C)), () => Temporal<Ev2>.Transitions((p, n) => !(p is Ev2.A && n is Ev2.C))),
		("TransitionsByType",
		() => Rx.Transitions((typeof(Ev2.A), typeof(Ev2.B)), (typeof(Ev2.B), typeof(Ev2.C)), (typeof(Ev2.C), typeof(Ev2.A))),
		() => Temporal<Ev2>.Transitions((typeof(Ev2.A), typeof(Ev2.B)), (typeof(Ev2.B), typeof(Ev2.C)), (typeof(Ev2.C), typeof(Ev2.A)))),
		("When", () => Rx.When(e => e is Ev2.A).Requires(e => e is Ev2.A { Id: not 1 }), () =>
			Temporal<Ev2>.When(e => e is Ev2.A).Requires(e => e is Ev2.A { Id: not 1 })),
		("AfterWithin",
		() => Rx.After<Ev2.A>().Then<Ev2.B>((a, b) => a.Id == b.Id).Within(Window),
		() => Temporal<Ev2>.After<Ev2.A>().Then<Ev2.B>((a, b) => a.Id == b.Id).Within(Window)),
		("AfterWithinEitherOf",
		() => Rx.After<Ev2.A>().Then<Ev2.B, Ev2.C>((a, b) => a.Id == b.Id, null).Within(Window),
		() => Temporal<Ev2>.After<Ev2.A>().Then<Ev2.B, Ev2.C>((a, b) => a.Id == b.Id, null).Within(Window)),
		("AfterWithinUnlessInterrupted",
		() => Rx.After<Ev2.A>().Then<Ev2.B>().Within(Window).UnlessInterruptedBy<Ev2.C>(),
		() => Temporal<Ev2>.After<Ev2.A>().Then<Ev2.B>().Within(Window).UnlessInterruptedBy<Ev2.C>()),
		("AfterWithinRequired", () => Rx.After<Ev2.B>().Then<Ev2.C>().Within(Window).Required(), () =>
			Temporal<Ev2>.After<Ev2.B>().Then<Ev2.C>().Within(Window).Required()),
		("AfterUntil",
		() => Rx.AfterUntil(e => e is Ev2.C, (t, e) => e is Ev2.A a && t is Ev2.C c && a.Id == c.Id, Window, e => e is Ev2.B { Id: 2 }),
		() => Temporal<Ev2>.AfterUntil(e => e is Ev2.C, (t, e) => e is Ev2.A a && t is Ev2.C c && a.Id == c.Id, Window, e => e is Ev2.B { Id: 2 })),
		("Eventually",
		() => Rx.Eventually<Ev2.B>(e => IdOf(e), e => e is Ev2.A),
		() => Temporal<Ev2>.Eventually<Ev2.B>(e => IdOf(e), e => e is Ev2.A)),
	];

	private static object IdOf(Ev2 e) => e switch
	{
		Ev2.A a => a.Id,
		Ev2.B b => b.Id,
		Ev2.C c => c.Id,
		_ => -1,
	};

	private static readonly Gen<(TimeSpan Delay, Ev2 Event)[]> Traces = Gen.Select(
		Gen.OneOfConst(0, 1, 5, 10, 29, 30, 31, 60).Select(s => TimeSpan.FromSeconds(s)),
		Gen.Int[0, 2],
		Gen.Int[0, 2],
		(delay, kind, id) =>
			(delay, Event: kind switch
			{
				0 => new Ev2.A(id),
				1 => new Ev2.B(id),
				_ => (Ev2)new Ev2.C(id),
			})
	).Array[0, 12];

	private static string Describe(Verdict v) => v.IsViolated ? $"violated@{v.ViolationAt:O}" : v.IsHolds ? "holds" : "pending";

	private static (string Reactive, string Core) Run((TimeSpan Delay, Ev2 Event)[] trace, Func<ReactiveConstraint<Ev2>> reactive, Func<TemporalRule<Ev2>> core)
	{
		var scheduler = new HistoricalScheduler();
		var subject = new Subject<Ev2>();
		var rx = reactive().Attach(subject, scheduler);
		var monitor = core().CreateMonitor();
		var now = scheduler.Now;

		foreach (var (delay, e) in trace)
		{
			scheduler.AdvanceBy(delay);
			now += delay;
			subject.OnNext(e);
			monitor.Observe(e, now);
		}
		// let every outstanding deadline expire, then end the stream: a closed run, as Trace.Check treats it
		var tail = TimeSpan.FromSeconds(100);
		scheduler.AdvanceBy(tail);
		now += tail;
		subject.OnCompleted();
		monitor.Tick(now);
		monitor.Finish(now);

		return (Describe(rx.Current), Describe(monitor.Current));
	}

	[Test]
	public void Every_operator_gives_the_same_verdict_and_violation_time_over_Rx_and_over_the_core_monitor()
	{
		foreach (var (name, reactive, core) in Pairs)
		{
			Traces.Sample(
				trace =>
				{
					var (rx, pure) = Run(trace, reactive, core);
					return rx == pure;
				},
				iter: 400,
				print: trace =>
					$"{name}: " + string.Join(
						", ",
						trace.Select(t => $"+{t.Delay.TotalSeconds}s {t.Event}")
					) + $"  =>  Rx {Run(trace, reactive, core).Reactive} vs core {Run(trace, reactive, core).Core}"
			);
		}
	}

	[Test]
	public void The_generator_actually_reaches_violations_holds_and_pending_for_each_operator()
	{
		// guards against a vacuous differential: if no trace ever violates a rule, agreeing on "pending" proves nothing
		foreach (var (name, reactive, core) in Pairs)
		{
			var outcomes = new HashSet<string>();
			var violated = 0;
			for (var i = 0; i < 600; i++)
			{
				var trace = Traces.Generate(new PCG(1u, (ulong)i + 1), null, out _);
				var verdict = Run(trace, reactive, core).Core;
				outcomes.Add(verdict.StartsWith("violated", StringComparison.Ordinal) ? "violated" : verdict);
				violated += verdict.StartsWith("violated", StringComparison.Ordinal) ? 1 : 0;
			}
			violated.Should().BeGreaterThan(10, $"{name} should be violated by a decent share of generated traces");
			// Eventually is decided when the closed run ends, so it is never left pending: it must reach "holds" instead.
			outcomes.Should().Contain(name == "Eventually" ? "holds" : "pending", $"{name} should also reach its other outcome on some traces");
		}
	}
}
