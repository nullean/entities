using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using TUnit;

// Same alias as ScriptTests.cs, for the same reason: the sync Constraints<T> is in unqualified scope.
using ReactiveConstraints = Nullean.Entities.Reactive.Constraints<Nullean.Entities.CsCheck.Tests.CounterEvent>;

namespace Nullean.Entities.CsCheck.Tests;

public class ScriptCoverageTests
{
	private static readonly ReactiveConstraintSet<CounterEvent> NeverNegative = ReactiveConstraintSet.From(
		("NeverNegative", ReactiveConstraints.Always<CounterEvent.Decremented>(e => e.Value >= 0))
	);

	[Test]
	public void Passes_when_every_expected_event_is_produced()
	{
		var script = Script.For<Counter>().Step("Increment", c => c.Increment()).Length(1, 3);
		var coverage = new Coverage<CounterEvent>().Expect("Incremented", e => e is CounterEvent.Incremented);
		var lines = new List<string>();

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, coverage, iter: 20, writeLine: lines.Add);

		act.Should().NotThrow();
		lines.Should().ContainSingle().Which.Should().Contain("Incremented");
	}

	[Test]
	public void Throws_VacuousPropertyException_when_an_expected_event_never_occurs()
	{
		// The constraint passes in every run, but Decremented is never reached, so passing proves nothing about it.
		var script = Script.For<Counter>().Step("Increment", c => c.Increment()).Length(1, 3);
		var coverage = new Coverage<CounterEvent>().Expect("Decremented", e => e is CounterEvent.Decremented);

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, coverage, iter: 20);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("\"Decremented\" never occurred");
	}

	[Test]
	public void Enforces_a_minimum_percentage_of_runs()
	{
		// Length(1, 3) with only increment steps: every run produces Incremented.
		var script = Script.For<Counter>().Step("Increment", c => c.Increment()).Length(1, 3);
		var always = new Coverage<CounterEvent>().Expect("Incremented", e => e is CounterEvent.Incremented, minPercentOfRuns: 100);
		var neverEnough = new Coverage<CounterEvent>().Expect("Big", e => e is CounterEvent.Incremented { Value: > 1000 }, minPercentOfRuns: 50);

		var ok = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, always, iter: 20);
		var bad = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, neverEnough, iter: 20);

		ok.Should().NotThrow();
		bad.Should().Throw<VacuousPropertyException>();
	}

	[Test]
	public void A_real_violation_still_surfaces_as_a_CsCheck_failure_not_a_vacuity_failure()
	{
		var script = Script.For<Counter>().Step("Decrement", c => c.Decrement());
		var coverage = new Coverage<CounterEvent>().Expect("Decremented", e => e is CounterEvent.Decremented);

		var act = () => script.SampleWith(_ => new Counter(), c => c.Events, NeverNegative, coverage, iter: 5);

		act.Should().Throw<CsCheckException>();
	}
}
