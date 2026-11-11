using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using TUnit;

// Nullean.Entities (sync) and Nullean.Entities.Reactive both declare a Constraints<T> static
// builder, and this file's own namespace nests inside Nullean.Entities — which puts the sync one
// in unqualified scope ahead of the `using` above, per C# lookup precedence. An alias to the
// closed reactive generic sidesteps the ambiguity without qualifying every call site.
using ReactiveConstraints = Nullean.Entities.Reactive.Constraints<Nullean.Entities.CsCheck.Tests.CounterEvent>;

namespace Nullean.Entities.CsCheck.Tests;

// ── Domain ───────────────────────────────────────────────────────────────────
// A deliberately tiny SUT: enough state to drive real Script<TSut> runs through CsCheck's actual
// generate/shrink/replay machinery without a domain big enough to obscure what's being proven.

public abstract record CounterEvent
{
	public sealed record Incremented(int Value) : CounterEvent;

	public sealed record Decremented(int Value) : CounterEvent;
}

internal sealed class Counter : IDisposable
{
	private readonly Subject<CounterEvent> _events = new();
	private int _value;

	public IObservable<CounterEvent> Events => _events;
	public int Value => _value;

	public void Dispose() => _events.Dispose();

	public void Increment()
	{
		_value++;
		_events.OnNext(new CounterEvent.Incremented(_value));
	}

	public void Decrement()
	{
		_value--;
		_events.OnNext(new CounterEvent.Decremented(_value));
	}

	public void IncrementBy(int n)
	{
		for (var i = 0; i < n; i++)
		{
			Increment();
		}
	}
}

public class ScriptSampleWithTests
{
	private static readonly ReactiveConstraintSet<CounterEvent> NeverNegative = ReactiveConstraintSet.From(
		("NeverNegative", ReactiveConstraints.Always<CounterEvent.Decremented>(e => e.Value >= 0))
	);

	[Test]
	public void PassingScriptNeverThrows()
	{
		// Only ever increments from zero — Value is always positive, so NeverNegative can't fire.
		var script = Script.For<Counter>().Step("Increment", c => c.Increment());

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 20);

		act.Should().NotThrow();
	}

	[Test]
	public void ViolatingScriptThrowsWithAReproducibleSeed()
	{
		// Only ever decrements from zero — the very first step drives Value to -1, so every
		// iteration fails on its first step. Deterministic: no need for iter to get lucky.
		var script = Script.For<Counter>().Step("Decrement", c => c.Decrement());

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 5);

		act.Should().Throw<CsCheckException>().WithMessage("*CsCheck_Seed*");
	}

	[Test]
	public void GeneratedArgumentStepDrawsAndAppliesAFreshArgumentEachTime()
	{
		// Step<TArg> — the argument (how much to increment by) is generated, not fixed. Value can
		// only grow, so NeverNegative can never fire regardless of which n is drawn.
		var script = Script.For<Counter>().Step("IncrementBy", Gen.Int[1, 20], (c, n) => c.IncrementBy(n));

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 30);

		act.Should().NotThrow();
	}

	[Test]
	public void EmptyScriptRefusesRatherThanSamplingNothing()
	{
		var script = Script.For<Counter>();

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 1);

		act.Should().Throw<InvalidOperationException>().WithMessage("*Step*");
	}
}

public class ScriptDelayTests
{
	private static readonly ReactiveConstraintSet<CounterEvent> DecrementFollowsIncrementQuickly = ReactiveConstraintSet.From(
		("DecrementFollowsIncrementQuickly", ReactiveConstraints
			.After<CounterEvent.Incremented>()
			.Then<CounterEvent.Decremented>()
			.Within(TimeSpan.FromSeconds(1))
			.Required())
	);

	[Test]
	public void DeadlineViolatesWhenAGeneratedDelayPushesVirtualTimePastTheWindow()
	{
		// Only ever increments, and every step (including the one after the first) waits 2 real
		// (virtual) seconds — well past the 1-second deadline the constraint allows before a
		// Decremented event must arrive. Exactly 2 steps: the second step's own delay is what
		// advances the scheduler far enough for the first step's timeout to actually fire — a
		// deadline check attached to the very last step in a run has nothing to advance time for it.
		var script = Script.For<Counter>().Step("Increment", c => c.Increment()).Delays(Gen.Const(TimeSpan.FromSeconds(2))).Length(2, 2);

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, DecrementFollowsIncrementQuickly, iter: 3);

		act.Should().Throw<CsCheckException>();
	}

	[Test]
	public void NoViolationWhenVirtualTimeNeverReachesTheDeadline()
	{
		// Same shape, but the fixed delay (0.1s) never lets virtual time cross the 1-second
		// deadline across 2 steps (0.2s total) — the constraint stays Pending, which is not a
		// violation.
		var script = Script.For<Counter>().Step("Increment", c => c.Increment()).Delays(Gen.Const(TimeSpan.FromSeconds(0.1))).Length(2, 2);

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, DecrementFollowsIncrementQuickly, iter: 3);

		act.Should().NotThrow();
	}
}

public class ScriptMetamorphicTests
{
	[Test]
	public void OrderOfCommutingIncrementsDoesNotMatter()
	{
		// IncrementBy(a) then IncrementBy(b) must land on the same value as IncrementBy(b) then
		// IncrementBy(a) — a genuine confluence property, checked against two fresh instances per
		// iteration rather than asserted by hand for one fixed (a, b).
		var script = Script.For<Counter>();

		var act =
			() => script.Metamorphic(
				() => new Counter(),
				Gen.Int[1, 50],
				(counter, a, b) =>
				{
					counter.IncrementBy(a);
					counter.IncrementBy(b);
				},
				(counter, a, b) =>
				{
					counter.IncrementBy(b);
					counter.IncrementBy(a);
				},
				(x, y) => x.Value == y.Value,
				iter: 30
			);

		act.Should().NotThrow();
	}

	[Test]
	public void OrderDependentPathsAreCaught()
	{
		// IncrementBy(a) then IncrementBy(b) does NOT land on the same value as
		// IncrementBy(a + b) followed by an extra, order-sensitive IncrementBy(1) only on one side
		// — a deliberately broken "confluence" to prove Metamorphic actually compares the two
		// instances rather than trivially passing.
		var script = Script.For<Counter>();

		var act =
			() => script.Metamorphic(
				() => new Counter(),
				Gen.Int[1, 50],
				(counter, a, b) =>
				{
					counter.IncrementBy(a);
					counter.IncrementBy(b);
				},
				(counter, a, b) =>
				{
					counter.IncrementBy(a);
					counter.IncrementBy(b);
					counter.Increment(); // the one extra step that breaks confluence
				},
				(x, y) => x.Value == y.Value,
				iter: 10
			);

		act.Should().Throw<CsCheckException>();
	}
}

public class ScriptRunWithTests
{
	private static readonly ReactiveConstraintSet<CounterEvent> NeverNegative = ReactiveConstraintSet.From(
		("NeverNegative", ReactiveConstraints.Always<CounterEvent.Decremented>(e => e.Value >= 0))
	);

	[Test]
	public async Task PassingRunEmitsOnlyCompletedWithPassedTrue()
	{
		var script = Script.For<Counter>().Step("Increment", c => c.Increment());

		var events = await script.RunWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 10).ToList();

		events.OfType<EntityEvent.IterationStarted>().Should().ContainSingle();
		events.OfType<EntityEvent.Completed>().Should().ContainSingle(completed => completed.Passed);
		events.OfType<EntityEvent.SmallerFailure>().Should().BeEmpty();
	}

	[Test]
	public async Task FailingRunEmitsSmallerFailureWithTheViolatedConstraintNameAndCompletedFalse()
	{
		var script = Script.For<Counter>().Step("Decrement", c => c.Decrement());

		var events = await script.RunWith(_ => new Counter(), c => c.Events, NeverNegative, iter: 5).ToList();

		var failure = events.OfType<EntityEvent.SmallerFailure>().Should().ContainSingle().Subject;
		failure.Violations.Should().Contain("NeverNegative");
		failure.Seed.Should().NotBeNullOrWhiteSpace();

		events.OfType<EntityEvent.ConstraintResolved>().Should().ContainSingle(resolved => resolved.Constraint == "NeverNegative");
		events.OfType<EntityEvent.Completed>().Should().ContainSingle(completed => !completed.Passed);
	}
}
