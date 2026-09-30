using System.Reactive.Subjects;
using AwesomeAssertions;
using Microsoft.Reactive.Testing;
using Nullean.Entities;

namespace Nullean.Entities.Reactive.Tests;

// Domain used across all tests
internal abstract record Ev
{
	public sealed record A(int Value) : Ev;

	public sealed record B(int Value) : Ev;

	public sealed record C(int Value) : Ev;
}

public class AlwaysTests
{
	[Test]
	public void Holds_when_no_events_violate()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Always<Ev.A>(a => a.Value > 0);
		var running = constraint.Named("pos").Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.A(2));

		running.Current.IsViolated.Should().BeFalse();
		running.Current.IsPending.Should().BeTrue(); // still pending until stream ends
	}

	[Test]
	public void Violated_on_first_failing_event()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Always<Ev.A>(a => a.Value > 0);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.A(-1)); // violates

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Non_matching_event_types_are_ignored()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Always<Ev.A>(a => a.Value > 0);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.B(-999)); // B, not A — ignored
		running.Current.IsViolated.Should().BeFalse();
	}
}

public class NeverTests
{
	[Test]
	public void Holds_when_consecutive_pair_does_not_match()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Never<Ev.A, Ev.B>((a, b) => a.Value == b.Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.B(2)); // different values — ok
		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Violated_when_consecutive_pair_matches()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Never<Ev.A, Ev.B>((a, b) => a.Value == b.Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(5));
		subject.OnNext(new Ev.B(5)); // same value — violated
		running.Current.IsViolated.Should().BeTrue();
	}
}

public class NeverTwiceTests
{
	[Test]
	public void Violated_when_same_key_appears_twice()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.NeverTwice<Ev.A>(a => a.Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.B(99)); // unrelated
		subject.OnNext(new Ev.A(1)); // duplicate key

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Holds_when_keys_are_distinct()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.NeverTwice<Ev.A>(a => a.Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.A(2));
		subject.OnNext(new Ev.A(3));

		running.Current.IsViolated.Should().BeFalse();
	}
}

public class TransitionsTests
{
	[Test]
	public void Allowlist_permits_legal_transitions()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Transitions((typeof(Ev.A), typeof(Ev.B)), (typeof(Ev.B), typeof(Ev.C)));
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(0));
		subject.OnNext(new Ev.B(0));
		subject.OnNext(new Ev.C(0));

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Allowlist_rejects_illegal_transition()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Transitions((typeof(Ev.A), typeof(Ev.B)));
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(0));
		subject.OnNext(new Ev.C(0)); // A→C not allowed

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Guard_overload_rejects_failing_transition()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Transitions((prev, next) => prev is Ev.A && next is Ev.B); // only A→B allowed
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(0));
		subject.OnNext(new Ev.B(0));
		subject.OnNext(new Ev.B(0)); // B→B not allowed

		running.Current.IsViolated.Should().BeTrue();
	}
}

public class TemporalTests
{
	[Test]
	public void Within_holds_when_next_arrives_in_time()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		ReactiveConstraint<Ev> constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>((a, b) => a.Value == b.Value).Within(TimeSpan.FromSeconds(5));
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks);
		subject.OnNext(new Ev.B(1)); // matches, within 5s

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Within_violated_when_timeout_expires()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		ReactiveConstraint<Ev> constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>().Within(TimeSpan.FromSeconds(5));
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(6).Ticks); // past the window, no B arrived

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void UnlessInterruptedBy_lifts_obligation_on_interrupt()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>().Within(TimeSpan.FromSeconds(5)).UnlessInterruptedBy<Ev.C>();
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
		subject.OnNext(new Ev.C(0)); // interruption — lifts obligation

		scheduler.AdvanceBy(TimeSpan.FromSeconds(4).Ticks); // past original window
		running.Current.IsViolated.Should().BeFalse();
		running.Current.IsHolds.Should().BeTrue();
	}
}

public class ReactiveConstraintSetTests
{
	private static readonly ReactiveConstraint<Ev> AlwaysPositive = Constraints<Ev>.Always<Ev.A>(a => a.Value > 0).Named("AlwaysPositive");
	private static readonly ReactiveConstraint<Ev> NoDuplicates = Constraints<Ev>.NeverTwice<Ev.A>(a => a.Value).Named("NoDuplicates");

	private static readonly ReactiveConstraintSet<Ev> All = ReactiveConstraintSet.From(("AlwaysPositive", AlwaysPositive), ("NoDuplicates", NoDuplicates));

	[Test]
	public void Attach_exposes_indexed_access_by_constraint()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		subject.OnNext(new Ev.A(-1)); // violates AlwaysPositive

		running[AlwaysPositive].Current.IsViolated.Should().BeTrue();
		running[NoDuplicates].Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Attach_exposes_indexed_access_by_name()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.A(1)); // duplicate

		running["NoDuplicates"].Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void State_reflects_the_latest_verdict_of_every_constraint()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);
		ConstraintSetState? last = null;
		running.State.Subscribe(s => last = s);

		subject.OnNext(new Ev.A(-1)); // violates AlwaysPositive only

		last.Should().NotBeNull();
		last!.Verdicts["AlwaysPositive"].IsViolated.Should().BeTrue();
		last.Verdicts["NoDuplicates"].IsViolated.Should().BeFalse();
	}
}

public class ConstraintSetReactiveExtensionsTests
{
	// Fully qualified: Nullean.Entities.Reactive.Tests nests (by dot-name) inside
	// Nullean.Entities.Reactive, which declares its OWN Constraints<TEvent> — that containing
	// namespace wins over `using Nullean.Entities;` for an unqualified Constraints<Ev.A>, so the
	// sync builder needs its namespace spelled out here.
	private static readonly Constraint<Ev.A> StaysPositive = global::Nullean
		.Entities
		.Constraints<Ev.A>
		.Check(a => a.Value > 0 ? [] : [Violation.Error($"Value {a.Value} is not positive")])
		.Named("StaysPositive");

	private static readonly ConstraintSet<Ev.A> SyncConstraints = ConstraintSet.From((nameof(StaysPositive), StaysPositive));

	private static readonly Constraint<Ev.A> NeverDecreases = global::Nullean
		.Entities
		.Constraints<Ev.A>
		.Transitions((prev, next) => next.Value >= prev.Value, "decreased");

	private static readonly ConstraintSet<Ev.A> SequentialSyncConstraints = ConstraintSet.From((nameof(NeverDecreases), NeverDecreases));

	[Test]
	public void ToReactive_lifts_a_point_in_time_ConstraintSet_onto_a_stream()
	{
		var subject = new Subject<Ev.A>();
		var running = SyncConstraints.ToReactive().Attach(subject);

		subject.OnNext(new Ev.A(1));
		running["StaysPositive"].Current.IsViolated.Should().BeFalse();

		subject.OnNext(new Ev.A(-1));
		running["StaysPositive"].Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void ToReactive_lifts_a_sequential_ConstraintSet_by_checking_consecutive_pairs()
	{
		var subject = new Subject<Ev.A>();
		var running = SequentialSyncConstraints.ToReactive().Attach(subject);

		subject.OnNext(new Ev.A(1)); // no previous yet — never triggers alone
		running["NeverDecreases"].Current.IsViolated.Should().BeFalse();

		subject.OnNext(new Ev.A(5)); // 1 -> 5, fine
		running["NeverDecreases"].Current.IsViolated.Should().BeFalse();

		subject.OnNext(new Ev.A(2)); // 5 -> 2, decreased
		running["NeverDecreases"].Current.IsViolated.Should().BeTrue();
	}
}
