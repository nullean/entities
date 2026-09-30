using System.Reactive.Subjects;
using AwesomeAssertions;

namespace Nullean.Entities.Reactive.Tests;

// Constraints<TEvent>.When(guard).Requires(requirement): a conditional constraint — only events
// matching the guard are checked against the requirement at all. Distinct from Always<TEvent>,
// which requires every event of a given payload type to satisfy a predicate: When/Requires guards
// on an arbitrary predicate over the full event union, not a type test.
public class WhenBuilderTests
{
	[Test]
	public void Holds_when_no_event_matches_the_guard()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.When(e => e is Ev.A).Requires(e => ((Ev.A)e).Value > 0);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.B(-1)); // guard doesn't match — requirement never runs
		subject.OnNext(new Ev.C(-1));

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Holds_when_guarded_events_satisfy_the_requirement()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.When(e => e is Ev.A a && a.Value > 0).Requires(e => e is Ev.A a && a.Value < 100);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(50));

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Violated_on_the_first_guarded_event_that_fails_the_requirement()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.When(e => e is Ev.A a && a.Value > 0).Requires(e => e is Ev.A a && a.Value < 100);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(50)); // guarded, satisfies
		subject.OnNext(new Ev.A(150)); // guarded, fails

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Ungated_events_never_trip_the_requirement()
	{
		var subject = new Subject<Ev>();
		// Guard only admits Ev.A; an Ev.B that would fail the same shape of check is irrelevant.
		var constraint = Constraints<Ev>.When(e => e is Ev.A).Requires(e => ((Ev.A)e).Value > 0);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.B(-999));

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Composes_into_a_ReactiveConstraintSet_like_any_other_reactive_constraint()
	{
		var conditional = Constraints<Ev>.When(e => e is Ev.A a && a.Value > 0).Requires(e => e is Ev.A a && a.Value < 100).Named("BoundedWhenPositive");
		var set = ReactiveConstraintSet.From(("BoundedWhenPositive", conditional));

		var subject = new Subject<Ev>();
		var running = set.Attach(subject);

		subject.OnNext(new Ev.A(150));

		running["BoundedWhenPositive"].Current.IsViolated.Should().BeTrue();
	}
}
