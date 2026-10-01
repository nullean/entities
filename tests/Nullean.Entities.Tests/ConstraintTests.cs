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

public class ConstraintChannelTests
{
	private static readonly Constraint<Item> StaysPositive = Constraints<Item>.Check(
		item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
	).Named("StaysPositive");

	private static readonly ConstraintSet<Item> ItemConstraints = ConstraintSet.From((nameof(StaysPositive), StaysPositive));

	[Test]
	public void Apply_succeeds_and_updates_Current()
	{
		var channel = ConstraintChannel.For(new Item(1)).Enforce(ItemConstraints);
		var result = channel.Apply(item => item with { Value = 5 });

		result.Should().BeOfType<Result<Item>.Ok>();
		channel.Current.Value.Should().Be(5);
	}

	[Test]
	public void Apply_fails_and_does_not_update_Current()
	{
		var channel = ConstraintChannel.For(new Item(1)).Enforce(ItemConstraints);
		var result = channel.Apply(item => item with { Value = -1 });

		result.Should().BeOfType<Result<Item>.Fail>();
		channel.Current.Value.Should().Be(1); // unchanged
	}
}
