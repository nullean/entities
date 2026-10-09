using System.Reactive.Subjects;
using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using TUnit;

// Same alias as ScriptTests.cs, for the same reason: the sync Constraints<T> is in unqualified scope.
using ReactiveConstraints = Nullean.Entities.Reactive.Constraints<Nullean.Entities.CsCheck.Tests.CounterEvent>;

namespace Nullean.Entities.CsCheck.Tests;

// Counter's Subject is not safe to call from several threads; this one serialises its own calls,
// which is what a SUT must do for SampleParallelWith to test the constraints rather than Rx.
internal sealed class LockedCounter
{
	private readonly object _gate = new();
	private readonly Subject<CounterEvent> _events = new();
	private int _value;

	public IObservable<CounterEvent> Events => _events;

	public void Increment()
	{
		lock (_gate)
		{
			_value++;
			_events.OnNext(new CounterEvent.Incremented(_value));
		}
	}
}

public class ScriptParallelTests
{
	private static readonly ReactiveConstraintSet<CounterEvent> NeverNegative = ReactiveConstraintSet.From(
		("NeverNegative", ReactiveConstraints.Always<CounterEvent.Incremented>(e => e.Value > 0))
	);

	[Test]
	public void Constraints_that_hold_under_concurrency_pass()
	{
		var script = Script.For<LockedCounter>().Step("Increment", c => c.Increment());

		var act = () => script.SampleParallelWith(_ => new LockedCounter(), c => c.Events, NeverNegative, iter: 20);

		act.Should().NotThrow();
	}

	[Test]
	public void A_script_without_steps_is_rejected()
	{
		var act = () => Script.For<LockedCounter>().SampleParallelWith(_ => new LockedCounter(), c => c.Events, NeverNegative, iter: 1);

		act.Should().Throw<InvalidOperationException>();
	}
}
