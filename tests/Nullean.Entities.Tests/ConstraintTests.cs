using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// Domain used across the synchronous constraint tests.
internal sealed record Item(int Value);

public class ConstraintSetTests
{
	// Constraints<T>.Check is the escape hatch for a message built from the value itself —
	// Always's fixed-message overload can't express "Value {item.Value} is not positive".
	private static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Check(
		item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
	).Named("AlwaysPositive");

	private static readonly Constraint<Item> NeverOverHundred = Constraints<Item>.Check(
		item => item.Value <= 100 ? [] : [Violation.Warning($"Value {item.Value} exceeds 100")]
	).Named("NeverOverHundred");

	private static readonly ConstraintSet<Item> All = ConstraintSet.From(
		(nameof(AlwaysPositive), AlwaysPositive),
		(nameof(NeverOverHundred), NeverOverHundred)
	);

	[Test]
	public void Validate_returns_no_violations_when_all_constraints_pass()
	{
		var violations = All.Validate(new Item(5));
		violations.Should().BeEmpty();
	}

	[Test]
	public void Validate_returns_a_violation_tagged_with_the_constraint_name()
	{
		var violations = All.Validate(new Item(-1));

		violations.Should().ContainSingle();
		violations[0].ConstraintId.Should().Be("AlwaysPositive");
		violations[0].Severity.Should().Be(Severity.Error);
	}

	[Test]
	public void Validate_can_report_more_than_one_violation()
	{
		var violations = All.Validate(new Item(200));
		violations.Should().ContainSingle(v => v.ConstraintId == "NeverOverHundred");
	}

	[Test]
	public void Apply_returns_Ok_when_no_constraints_are_violated()
	{
		var result = All.Apply(new Item(1), current => current with { Value = 2 });
		result.Should().BeOfType<Result<Item>.Ok>();
	}

	[Test]
	public void Apply_returns_Fail_when_a_constraint_is_violated()
	{
		var result = All.Apply(new Item(1), _ => new Item(-5));

		result.Should().BeOfType<Result<Item>.Fail>();
		((Result<Item>.Fail)result).Violations.Should().NotBeEmpty();
	}

	[Test]
	public void Fail_keeps_the_attempted_value_for_a_caller_that_matches_on_the_record_directly()
	{
		var result = All.Apply(new Item(1), _ => new Item(-5));

		var fail = result.Should().BeOfType<Result<Item>.Fail>().Subject;
		fail.Attempted.Value.Should().Be(-5);
	}

	[Test]
	public void ApplyOrThrow_returns_the_mutated_value_when_valid()
	{
		var next = All.ApplyOrThrow(new Item(1), current => current with { Value = 2 });
		next.Value.Should().Be(2);
	}

	[Test]
	public void ApplyOrThrow_throws_when_a_constraint_is_violated()
	{
		var act = () => All.ApplyOrThrow(new Item(1), _ => new Item(-5));
		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	public void Result_Match_dispatches_to_the_ok_branch()
	{
		var ok = All.Apply(new Item(1), _ => new Item(2));
		var called = false;
		ok.Match<Item?>(
			v =>
			{
				called = true;
				return v;
			},
			_ => null
		);
		called.Should().BeTrue();
	}

	[Test]
	public void Result_Match_dispatches_to_the_fail_branch()
	{
		var fail = All.Apply(new Item(1), _ => new Item(-1));
		var failCalled = false;
		fail.Match<Item?>(v => v, _ =>
		{
			failCalled = true;
			return null;
		});
		failCalled.Should().BeTrue();
	}
}

public class SequentialConstraintTests
{
	// Sequential — needs both the value before the mutation and the value after it. Meaningless
	// for a standalone instance, so Validate skips it entirely (see Skipped_by_Validate below).
	private static readonly Constraint<Item> NeverDecreases = Constraints<Item>.Transitions(
		(prev, next) => next.Value >= prev.Value,
		"Value must never decrease"
	);

	private static readonly ConstraintSet<Item> All = ConstraintSet.From((nameof(NeverDecreases), NeverDecreases));

	[Test]
	public void Apply_holds_when_the_mutation_increases_the_value()
	{
		var result = All.Apply(new Item(1), current => current with { Value = 2 });
		result.Should().BeOfType<Result<Item>.Ok>();
	}

	[Test]
	public void Apply_is_violated_when_the_mutation_decreases_the_value()
	{
		var result = All.Apply(new Item(5), current => current with { Value = 1 });
		result.Should().BeOfType<Result<Item>.Fail>();
	}

	[Test]
	public void Skipped_by_Validate_because_a_standalone_instance_has_no_previous()
	{
		// If Validate ran this constraint at all it would need some stand-in for "previous" —
		// this proves it doesn't even try, for any value.
		All.Validate(new Item(-1000)).Should().BeEmpty();
	}
}

public class IdempotentConstraintTests
{
	// Applying "reset to zero" once or twice from the same starting value gives the same result —
	// the standard f(f(x)) == f(x) law.
	private static readonly Constraint<Item> ResetIsIdempotent = Constraints<Item>.Idempotent(item => item with { Value = 0 }, "Reset must be idempotent.");

	// Applying "increment" twice from the same starting value does NOT give the same result as
	// applying it once — the counter-example that proves the shape actually checks something.
	private static readonly Constraint<Item> IncrementIsIdempotent = Constraints<Item>.Idempotent(
		item => item with { Value = item.Value + 1 },
		"Increment must be idempotent."
	);

	[Test]
	public void Holds_for_a_genuinely_idempotent_action()
	{
		var violations = ConstraintSet.From((nameof(ResetIsIdempotent), ResetIsIdempotent)).Validate(new Item(5));
		violations.Should().BeEmpty();
	}

	[Test]
	public void Is_violated_for_an_action_that_is_not_idempotent()
	{
		var violations = ConstraintSet.From((nameof(IncrementIsIdempotent), IncrementIsIdempotent)).Validate(new Item(5));

		violations.Should().ContainSingle();
		violations[0].ConstraintId.Should().Be(nameof(IncrementIsIdempotent));
		violations[0].Evidence.Should().Be((new Item(6), new Item(7)));
	}

	[Test]
	public void Runs_from_Validate_because_it_only_needs_the_value_being_checked()
	{
		// Unlike Monotonic/Conserves, Idempotent is PointInTime — the action itself supplies both
		// "once" and "twice", so a standalone instance is enough; no mutation needed.
		ConstraintSet.From((nameof(ResetIsIdempotent), ResetIsIdempotent)).Validate(new Item(-1000)).Should().BeEmpty();
	}
}

public class MonotonicConstraintTests
{
	private static readonly Constraint<Item> NeverDecreases = Constraints<Item>.Monotonic(item => item.Value, MonotonicDirection.NonDecreasing);

	private static readonly Constraint<Item> NeverIncreases = Constraints<Item>.Monotonic(item => item.Value, MonotonicDirection.NonIncreasing);

	[Test]
	public void NonDecreasing_holds_when_the_mutation_grows_the_value()
	{
		var result = ConstraintSet.From((nameof(NeverDecreases), NeverDecreases)).Apply(new Item(1), current => current with { Value = 5 });
		result.Should().BeOfType<Result<Item>.Ok>();
	}

	[Test]
	public void NonDecreasing_holds_when_the_mutation_leaves_the_value_unchanged()
	{
		var result = ConstraintSet.From((nameof(NeverDecreases), NeverDecreases)).Apply(new Item(1), current => current with { Value = 1 });
		result.Should().BeOfType<Result<Item>.Ok>();
	}

	[Test]
	public void NonDecreasing_is_violated_when_the_mutation_shrinks_the_value()
	{
		var result = ConstraintSet.From((nameof(NeverDecreases), NeverDecreases)).Apply(new Item(5), current => current with { Value = 1 });

		result.Should().BeOfType<Result<Item>.Fail>();
		var fail = (Result<Item>.Fail)result;
		fail.Violations.Should().ContainSingle();
		fail.Violations[0].Evidence.Should().Be((5, 1));
	}

	[Test]
	public void NonIncreasing_is_violated_when_the_mutation_grows_the_value()
	{
		var result = ConstraintSet.From((nameof(NeverIncreases), NeverIncreases)).Apply(new Item(1), current => current with { Value = 5 });
		result.Should().BeOfType<Result<Item>.Fail>();
	}

	[Test]
	public void Is_step_scoped_by_construction_because_Validate_never_runs_a_Sequential_constraint()
	{
		// A Monotonic constraint that would fail on a decrease still reports nothing from
		// Validate — it only ever fires from the specific mutation (Apply) it's attached to, which
		// is what makes it safe to attach only to the step(s) meant to hold it (e.g. AddLine) and
		// not the type's whole Constraints set.
		ConstraintSet.From((nameof(NeverDecreases), NeverDecreases)).Validate(new Item(-1000)).Should().BeEmpty();
	}
}

public class ConservesConstraintTests
{
	// The first inherently pair-valued shape: T is a (From, To) tuple, and the conserved quantity
	// is computed across both halves — moving a value from one to the other must not create or
	// destroy it.
	private static readonly Constraint<(Item From, Item To)> TotalIsConserved = Constraints<(Item From, Item To)>.Conserves(
		p => p.From.Value + p.To.Value,
		"Moving a value must not create or destroy it."
	);

	private static readonly ConstraintSet<(Item From, Item To)> All = ConstraintSet.From((nameof(TotalIsConserved), TotalIsConserved));

	[Test]
	public void Holds_when_a_transfer_moves_value_between_the_two_halves_without_changing_the_total()
	{
		var result = All.Apply((new Item(10), new Item(0)), current => (current.From with { Value = 7 }, current.To with { Value = 3 }));
		result.Should().BeOfType<Result<(Item, Item)>.Ok>();
	}

	[Test]
	public void Is_violated_when_a_transfer_changes_the_total()
	{
		var result = All.Apply((new Item(10), new Item(0)), current => (current.From with { Value = 7 }, current.To with { Value = 4 }));

		result.Should().BeOfType<Result<(Item, Item)>.Fail>();
		var fail = (Result<(Item, Item)>.Fail)result;
		fail.Violations.Should().ContainSingle();
		fail.Violations[0].Evidence.Should().Be((10, 11));
	}

	[Test]
	public void Skipped_by_Validate_because_a_standalone_pair_has_no_previous()
	{
		All.Validate((new Item(1), new Item(2))).Should().BeEmpty();
	}
}

public class OptionalMessageDefaultsTests
{
	// One of each shape, every one built with the message omitted — proves the self-referencing
	// closure pattern (message ?? $"{self.Name ?? ...} ...") resolves self.Name correctly once
	// .Named(...)/ConstraintSet.From's (nameof, field) tuple has run, for every builder that now
	// has this default (Always, Transitions, Idempotent, Conserves, WhenBuilder.Requires) — plus
	// the pre-existing Monotonic, included here so all six are asserted the same way in one place.

	private static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(item => item.Value > 0);
	private static readonly Constraint<Item> NeverDecreases = Constraints<Item>.Transitions((prev, next) => next.Value >= prev.Value);
	private static readonly Constraint<Item> ResetIsIdempotent = Constraints<Item>.Idempotent(item => item with { Value = 0 });
	private static readonly Constraint<Item> NonDecreasing = Constraints<Item>.Monotonic(item => item.Value, MonotonicDirection.NonDecreasing);

	private static readonly Constraint<(Item From, Item To)> TotalIsConserved = Constraints<(Item From, Item To)>.Conserves(p => p.From.Value + p.To.Value);

	private static readonly Constraint<Item> ConfirmedRequiresPositive = Constraints<Item>.When(item => item.Value != 0).Requires(item => item.Value > 0);

	private static readonly ConstraintSet<Item> PointInTime = ConstraintSet.From(
		(nameof(AlwaysPositive), AlwaysPositive),
		(nameof(ResetIsIdempotent), ResetIsIdempotent),
		(nameof(ConfirmedRequiresPositive), ConfirmedRequiresPositive)
	);

	private static readonly ConstraintSet<Item> Sequential = ConstraintSet.From(
		(nameof(NeverDecreases), NeverDecreases),
		(nameof(NonDecreasing), NonDecreasing)
	);

	private static readonly ConstraintSet<(Item From, Item To)> Pairwise = ConstraintSet.From((nameof(TotalIsConserved), TotalIsConserved));

	[Test]
	public void Always_without_a_message_names_itself_and_attaches_the_offending_value_as_evidence()
	{
		var violations = PointInTime.Validate(new Item(-1));

		var violation = violations.Should().ContainSingle(v => v.ConstraintId == nameof(AlwaysPositive)).Subject;
		violation.Message.Should().Be("AlwaysPositive was violated.");
		violation.Evidence.Should().Be(new Item(-1));
	}

	[Test]
	public void Transitions_without_a_message_names_itself_and_attaches_the_prev_next_pair_as_evidence()
	{
		var result = Sequential.Apply(new Item(5), _ => new Item(1));

		var fail = result.Should().BeOfType<Result<Item>.Fail>().Subject;
		var violation = fail.Violations.Should().ContainSingle(v => v.ConstraintId == nameof(NeverDecreases)).Subject;
		violation.Message.Should().Be("NeverDecreases does not allow the transition from Item { Value = 5 } to Item { Value = 1 }.");
		violation.Evidence.Should().Be((new Item(5), new Item(1)));
	}

	[Test]
	public void Idempotent_without_a_message_names_itself()
	{
		var violations = PointInTime.Validate(new Item(5));

		violations.Should().NotContain(v => v.ConstraintId == nameof(ResetIsIdempotent));

		var notIdempotent = Constraints<Item>.Idempotent(item => item with { Value = item.Value + 1 }).Named("IncrementIsIdempotent");
		var fail = ConstraintSet.From(("IncrementIsIdempotent", notIdempotent)).Validate(new Item(5));
		fail.Should().ContainSingle().Which.Message.Should().Be("IncrementIsIdempotent is not idempotent.");
	}

	[Test]
	public void Conserves_without_a_message_names_itself_and_attaches_before_after_as_evidence()
	{
		var result = Pairwise.Apply((new Item(10), new Item(0)), current => (current.From with { Value = 7 }, current.To with { Value = 4 }));

		var fail = result.Should().BeOfType<Result<(Item, Item)>.Fail>().Subject;
		var violation = fail.Violations.Should().ContainSingle().Subject;
		violation.Message.Should().Be("TotalIsConserved did not conserve its value (was 10, became 11).");
		violation.Evidence.Should().Be((10, 11));
	}

	[Test]
	public void WhenBuilder_Requires_without_a_message_names_itself()
	{
		var violations = PointInTime.Validate(new Item(-5));

		var violation = violations.Should().ContainSingle(v => v.ConstraintId == nameof(ConfirmedRequiresPositive)).Subject;
		violation.Message.Should().Be("ConfirmedRequiresPositive was violated.");
		violation.Evidence.Should().Be(new Item(-5));
	}
}

public class TransitionBuilderTests
{
	private enum Status
	{
		Pending,
		Confirmed,
		Shipped,
		Cancelled
	}

	private sealed record Order(Status Status);

	// The declarative counterpart to Constraints<T>.Transitions(Func<T, T, bool>, string?) — one
	// Allow(from, to) edge per legal move instead of a hand-written tuple pattern-match.
	private static readonly Constraint<Order> LegalTransitions = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(Status.Pending, Status.Confirmed)
		.Allow(Status.Confirmed, Status.Shipped)
		.Allow(Status.Pending, Status.Cancelled)
		.Allow(Status.Confirmed, Status.Cancelled);

	private static readonly ConstraintSet<Order> All = ConstraintSet.From((nameof(LegalTransitions), LegalTransitions));

	[Test]
	public void Holds_for_an_allowed_edge()
	{
		var result = All.Apply(new Order(Status.Pending), current => current with { Status = Status.Confirmed });
		result.Should().BeOfType<Result<Order>.Ok>();
	}

	[Test]
	public void Is_violated_for_an_edge_not_in_the_allow_list()
	{
		var result = All.Apply(new Order(Status.Pending), current => current with { Status = Status.Shipped });

		var fail = result.Should().BeOfType<Result<Order>.Fail>().Subject;
		var violation = fail.Violations.Should().ContainSingle().Subject;
		violation.Message.Should().Be("LegalTransitions does not allow the transition from Pending to Shipped.");
		violation.Evidence.Should().Be((Status.Pending, Status.Shipped));
	}

	[Test]
	public void Skipped_by_Validate_because_a_standalone_instance_has_no_previous()
	{
		All.Validate(new Order(Status.Cancelled)).Should().BeEmpty();
	}
}

public class ConstraintChannelTests
{
	private static readonly Constraint<Item> StaysPositive = Constraints<Item>.Check(
		item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
	).Named("StaysPositive");

	private static readonly ConstraintSet<Item> ItemConstraints = ConstraintSet.From((nameof(StaysPositive), StaysPositive));

	[Test]
	public void Apply_returns_Applied_and_updates_Current()
	{
		var channel = ConstraintChannel.For(new Item(1)).Enforce(ItemConstraints);
		var result = channel.Apply(item => item with { Value = 5 });

		result.Should().BeOfType<ChannelResult<Item>.Applied>();
		channel.Current.Value.Should().Be(5);
	}

	[Test]
	public void Apply_returns_Failure_and_does_not_update_Current()
	{
		var channel = ConstraintChannel.For(new Item(1)).Enforce(ItemConstraints);
		var result = channel.Apply(item => item with { Value = -1 });

		var failure = result.Should().BeOfType<ChannelResult<Item>.Failure>().Subject;
		failure.Violations.Should().NotBeEmpty();
		channel.Current.Value.Should().Be(1); // unchanged
	}

	[Test]
	public void Apply_returns_Noop_when_mutation_produces_the_same_value()
	{
		var channel = ConstraintChannel.For(new Item(5)).Enforce(ItemConstraints);
		var result = channel.Apply(item => item); // identity — nothing changes

		var noop = result.Should().BeOfType<ChannelResult<Item>.Noop>().Subject;
		noop.Value.Value.Should().Be(5);
		channel.Current.Value.Should().Be(5);
	}

	[Test]
	public void Apply_Noop_does_not_run_any_constraints()
	{
		// A constraint that always fails — if it ran, the result would be Failure, not Noop.
		var alwaysFail = Constraints<Item>.Always(_ => false, "never passes").Named("AlwaysFail");
		var constraints = ConstraintSet.From((nameof(alwaysFail), alwaysFail));
		var channel = ConstraintChannel.For(new Item(5)).Enforce(constraints);

		var result = channel.Apply(item => item); // identity — short-circuits before running constraints
		result.Should().BeOfType<ChannelResult<Item>.Noop>();
	}
}
