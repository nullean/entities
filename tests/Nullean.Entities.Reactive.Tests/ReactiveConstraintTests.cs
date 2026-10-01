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

public class EventuallyTests
{
	// scope is called on both the opening (from) event and the closing (T) event — see
	// PredicateBasedOperatorTests.Scope for the same reasoning.
	private static object Scope(Ev e) => e switch
	{
		Ev.A a => a.Value,
		Ev.B b => b.Value,
		Ev.C c => c.Value,
		_ => throw new ArgumentOutOfRangeException(nameof(e)),
	};

	[Test]
	public void Holds_when_every_opened_scope_is_closed_before_completion()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Eventually<Ev.B>(scope: Scope, from: e => e is Ev.A);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.B(1));
		subject.OnCompleted();

		running.Current.IsHolds.Should().BeTrue();
	}

	[Test]
	public void Violated_on_completion_with_a_scope_still_open()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Eventually<Ev.B>(scope: Scope, from: e => e is Ev.A);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1)); // opens scope 1 — no B ever arrives
		subject.OnCompleted();

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Stays_pending_while_the_stream_has_not_completed_even_with_a_scope_open()
	{
		// The only point "never" can be told apart from "not yet" is OnCompleted — an open scope
		// mid-stream must not read as a violation.
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Eventually<Ev.B>(scope: Scope, from: e => e is Ev.A);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));

		running.Current.IsPending.Should().BeTrue();
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
	public void Multi_trigger_second_violation_is_detected()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		ReactiveConstraint<Ev> constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>((a, b) => a.Value == b.Value).Within(TimeSpan.FromSeconds(5));
		var running = constraint.Attach(subject, scheduler);

		// First trigger resolves fine (A(1) → B(1))
		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
		subject.OnNext(new Ev.B(1));
		running.Current.IsViolated.Should().BeFalse();

		// Second trigger fires but times out — this is the violation the old code would miss
		subject.OnNext(new Ev.A(2));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(6).Ticks); // past the window, no B(2)

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Required_violated_when_trigger_never_fires()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>().Within(TimeSpan.FromSeconds(5)).Required();
		var running = constraint.Attach(subject, scheduler);

		// No A trigger ever fires — stream completes without a trigger
		subject.OnCompleted();

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Required_holds_when_trigger_fires_and_resolves()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>().Within(TimeSpan.FromSeconds(5)).Required();
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
		subject.OnNext(new Ev.B(1)); // resolves fine
		subject.OnCompleted();

		// Trigger fired and resolved — no violation
		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Required_violated_when_trigger_fires_but_times_out()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.After<Ev.A>().Then<Ev.B>().Within(TimeSpan.FromSeconds(5)).Required();
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(6).Ticks); // timeout

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
															// With multi-trigger semantics, Holds is never emitted mid-stream — the constraint
															// stays Pending (still monitoring for future triggers). Absence of Violated is the signal.
		running.Current.IsViolated.Should().BeFalse();
		running.Current.IsPending.Should().BeTrue();
	}
}

// The predicate-based overloads (Never/NeverTwice/Always/Eventually/AfterUntil, no type
// parameters) exist for event shapes where a case type can't satisfy `where T : TEvent` at all —
// C# 11 unions. They work identically for a plain record hierarchy, so exercising them against Ev
// here proves the same behaviour as the typed overloads above without requiring a net11.0-only
// test project.
public class PredicateBasedOperatorTests
{
	[Test]
	public void Never_violated_when_consecutive_pair_matches()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Never(e => e is Ev.A, e => e is Ev.B, (a, b) => ((Ev.A)a).Value == ((Ev.B)b).Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(5));
		subject.OnNext(new Ev.B(5)); // same value — violated

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Never_still_reports_a_false_positive_when_a_third_type_sits_between_them()
	{
		// Same filter-then-adjacency caveat as the typed overload: a C between A and B is filtered
		// out of the {isT1,isT2} view entirely, so A/B still look consecutive here. This is parity
		// with Never<T1,T2>, not a claim the predicate-based overload fixes it — see the doc comment.
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Never(e => e is Ev.A, e => e is Ev.B, (a, b) => ((Ev.A)a).Value == ((Ev.B)b).Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(5));
		subject.OnNext(new Ev.C(0)); // filtered out entirely — not T1/T2
		subject.OnNext(new Ev.B(5));

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void NeverTwice_violated_when_same_key_appears_twice()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.NeverTwice(e => e is Ev.A, e => ((Ev.A)e).Value);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.B(99)); // unrelated, ignored by isRelevant
		subject.OnNext(new Ev.A(1)); // duplicate key

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Always_violated_on_first_failing_relevant_event()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Always(e => e is Ev.A, e => ((Ev.A)e).Value > 0);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.B(-999)); // not relevant — ignored
		running.Current.IsViolated.Should().BeFalse();

		subject.OnNext(new Ev.A(-1)); // relevant and fails
		running.Current.IsViolated.Should().BeTrue();
	}

	// scope is called on both the opening (from) event and the closing (isTarget) event — it must
	// extract a key common to both, so it switches over every case that can appear rather than
	// casting to just Ev.A the way from/isTarget's own predicates do.
	private static object Scope(Ev e) => e switch
	{
		Ev.A a => a.Value,
		Ev.B b => b.Value,
		Ev.C c => c.Value,
		_ => throw new ArgumentOutOfRangeException(nameof(e)),
	};

	[Test]
	public void Eventually_holds_when_every_opened_scope_is_closed_before_completion()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Eventually(scope: Scope, from: e => e is Ev.A, isTarget: e => e is Ev.B);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnNext(new Ev.B(1)); // same scope key as A(1) — resolves it
		subject.OnCompleted();

		running.Current.IsHolds.Should().BeTrue();
	}

	[Test]
	public void Eventually_violated_on_completion_with_scopes_still_open()
	{
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.Eventually(scope: Scope, from: e => e is Ev.A, isTarget: e => e is Ev.B);
		var running = constraint.Attach(subject);

		subject.OnNext(new Ev.A(1));
		subject.OnCompleted(); // no B ever arrived — scope 1 never closed

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void AfterUntil_holds_when_a_matching_next_arrives_in_time()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.AfterUntil(
			isTrigger: e => e is Ev.A,
			isNext: (trigger, e) => e is Ev.B b && ((Ev.A)trigger).Value == b.Value,
			window: TimeSpan.FromSeconds(5)
		);
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks);
		subject.OnNext(new Ev.B(1));

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void AfterUntil_violated_when_the_window_expires_with_nothing_matching()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.AfterUntil(isTrigger: e => e is Ev.A, isNext: (_, e) => e is Ev.B, window: TimeSpan.FromSeconds(5));
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(6).Ticks);

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void AfterUntil_interrupt_lifts_the_obligation()
	{
		var scheduler = new TestScheduler();
		var subject = new Subject<Ev>();
		var constraint = Constraints<Ev>.AfterUntil(
			isTrigger: e => e is Ev.A,
			isNext: (_, e) => e is Ev.B,
			window: TimeSpan.FromSeconds(5),
			isInterrupt: e => e is Ev.C
		);
		var running = constraint.Attach(subject, scheduler);

		subject.OnNext(new Ev.A(1));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
		subject.OnNext(new Ev.C(0)); // interruption — lifts obligation

		scheduler.AdvanceBy(TimeSpan.FromSeconds(4).Ticks); // past original window
		running.Current.IsViolated.Should().BeFalse();
		running.Current.IsPending.Should().BeTrue();
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
		last.Verdicts["AlwaysPositive"].IsViolated.Should().BeTrue();
		last.Verdicts["NoDuplicates"].IsViolated.Should().BeFalse();
	}

	[Test]
	public void AnyViolated_is_false_until_something_actually_violates()
	{
		// This is the cheap synchronous check Nullean.Entities.CsCheck's SampleWith uses as
		// CsCheck's own `equal` — has to start false, or every generated run would fail immediately.
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		running.AnyViolated.Should().BeFalse();

		subject.OnNext(new Ev.A(1));
		running.AnyViolated.Should().BeFalse();
	}

	[Test]
	public void AnyViolated_becomes_true_the_moment_one_constraint_violates()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		subject.OnNext(new Ev.A(-1)); // violates AlwaysPositive only

		running.AnyViolated.Should().BeTrue();
	}

	[Test]
	public void Violations_lists_only_the_constraints_currently_in_a_violated_state()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		subject.OnNext(new Ev.A(-1)); // violates AlwaysPositive
		subject.OnNext(new Ev.A(2));
		subject.OnNext(new Ev.A(2)); // duplicate — violates NoDuplicates too

		running.Violations.Should().HaveCount(2);
		running.Violations.Select(v => v.Name).Should().BeEquivalentTo(["AlwaysPositive", "NoDuplicates"]);
	}

	[Test]
	public void Violations_is_empty_when_nothing_has_violated()
	{
		var subject = new Subject<Ev>();
		var running = All.Attach(subject);

		subject.OnNext(new Ev.A(1));

		running.Violations.Should().BeEmpty();
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
	public void ToReactive_gives_time_aware_constraints_the_schedulers_clock()
	{
		var scheduler = new TestScheduler();
		var fresh = global::Nullean.Entities.Constraints<Ev.A>.NotOlderThan(_ => DateTimeOffset.MinValue, TimeSpan.FromTicks(100));
		var set = ConstraintSet.From((nameof(fresh), fresh));
		var subject = new Subject<Ev.A>();
		var running = set.ToReactive().Attach(subject, scheduler);

		scheduler.AdvanceTo(50);
		subject.OnNext(new Ev.A(1));
		running["fresh"].Current.IsViolated.Should().BeFalse();

		scheduler.AdvanceTo(200);
		subject.OnNext(new Ev.A(2));
		running["fresh"].Current.IsViolated.Should().BeTrue();
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
