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
