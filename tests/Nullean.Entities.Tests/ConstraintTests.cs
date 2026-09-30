using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// Domain used across the synchronous constraint tests.
internal sealed record Item(int Value);

public class ConstraintSetTests
{
	private static readonly Constraint<Item> AlwaysPositive = new(
		"AlwaysPositive",
		"Value must stay positive.",
		item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
	);

	private static readonly Constraint<Item> NeverOverHundred = new(
		"NeverOverHundred",
		"Value must not exceed 100.",
		item => item.Value <= 100 ? [] : [Violation.Warning($"Value {item.Value} exceeds 100")]
	);

	private static readonly ConstraintSet<Item> All = ConstraintSet.From(AlwaysPositive, NeverOverHundred);

	[Test]
	public void Validate_returns_no_violations_when_all_constraints_pass()
	{
		var violations = All.Validate(new Item(5));
		violations.Should().BeEmpty();
	}

	[Test]
	public void Validate_returns_a_violation_tagged_with_the_constraint_id()
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
		ok.Match(
			v =>
			{
				called = true;
				return v;
			},
			(v, _) => v
		);
		called.Should().BeTrue();
	}

	[Test]
	public void Result_Match_dispatches_to_the_fail_branch()
	{
		var fail = All.Apply(new Item(1), _ => new Item(-1));
		var failCalled = false;
		fail.Match(v => v, (v, _) =>
		{
			failCalled = true;
			return v;
		});
		failCalled.Should().BeTrue();
	}
}

public class ConstraintChannelTests
{
	private static readonly Constraint<Item> StaysPositive = new(
		"StaysPositive",
		"Value must stay positive.",
		item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
	);

	private static readonly ConstraintSet<Item> ItemConstraints = ConstraintSet.From(StaysPositive);

	[Test]
	public void Apply_succeeds_and_updates_Current()
	{
		var channel = ConstraintChannel.Enforce(new Item(1), ItemConstraints);
		var result = channel.Apply(item => item with { Value = 5 });

		result.Should().BeOfType<Result<Item>.Ok>();
		channel.Current.Value.Should().Be(5);
	}

	[Test]
	public void Apply_fails_and_does_not_update_Current()
	{
		var channel = ConstraintChannel.Enforce(new Item(1), ItemConstraints);
		var result = channel.Apply(item => item with { Value = -1 });

		result.Should().BeOfType<Result<Item>.Fail>();
		channel.Current.Value.Should().Be(1); // unchanged
	}
}
