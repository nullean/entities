using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// The always-valid pattern: a private constructor and a factory that returns Result<T> from the rules.
internal sealed record Quantity
{
	private Quantity(int amount) => Amount = amount;

	public int Amount { get; }

	private static readonly Constraint<Quantity> Positive = Constraints<Quantity>.Always(q => q.Amount > 0, "Quantity must be positive");
	private static readonly Constraint<Quantity> Big = Constraints<Quantity>.Always(q => q.Amount <= 100, "Quantity is unusually large").WithSeverity(
		Severity.Warning
	);

	public static readonly ConstraintSet<Quantity> Rules = ConstraintSet.From((nameof(Positive), Positive), (nameof(Big), Big));

	public static Result<Quantity> Create(int amount) => Rules.Validated(new Quantity(amount));
}

internal sealed record Label(string Text)
{
	public static readonly ConstraintSet<Label> Rules = ConstraintSet.From(
		("not-blank", Constraints<Label>.Always(l => !string.IsNullOrWhiteSpace(l.Text), "Label is blank"))
	);

	public static Result<Label> Create(string text) => Rules.Validated(new Label(text));
}

public class ConstructionTests
{
	[Test]
	public void Validated_is_Ok_for_a_valid_value()
	{
		Quantity.Create(5).Should().BeOfType<Result<Quantity>.Ok>();
	}

	[Test]
	public void Validated_fails_on_an_error_rule_and_keeps_the_attempted_value()
	{
		var fail = Quantity.Create(0).Should().BeOfType<Result<Quantity>.Fail>().Subject;

		fail.Violations.Should().ContainSingle(v => v.Message == "Quantity must be positive");
		fail.Attempted.Amount.Should().Be(0);
	}

	[Test]
	public void Validated_lets_a_warning_through()
	{
		Quantity.Create(500).IsOk.Should().BeTrue();
	}

	[Test]
	public void Ensure_returns_the_value_or_throws_the_violations()
	{
		Quantity.Rules.Ensure(Quantity.Create(5).Unwrap()).Amount.Should().Be(5);

		var act = () => Label.Rules.Ensure(new Label(" "));
		act.Should().Throw<ConstraintViolationException>().Which.Violations.Should().ContainSingle(v => v.Message == "Label is blank");
	}

	[Test]
	public void Map_and_Bind_carry_a_value_and_stop_at_the_first_failure()
	{
		Quantity.Create(4).Map(q => q.Amount * 2).GetValueOrDefault(-1).Should().Be(8);
		Quantity.Create(4).Bind(q => Label.Create(new string('x', q.Amount))).IsOk.Should().BeTrue();

		var failed = Quantity.Create(0).Bind(q => Label.Create("never reached"));
		failed.IsFail.Should().BeTrue();
		failed.ViolationsOrEmpty.Should().ContainSingle(v => v.Message == "Quantity must be positive");
	}

	[Test]
	public void Query_syntax_short_circuits()
	{
		var ok = from q in Quantity.Create(3)
				 from l in Label.Create("pcs")
				 select $"{q.Amount} {l.Text}";
		ok.GetValueOrDefault("").Should().Be("3 pcs");

		var bad = from q in Quantity.Create(0)
				  from l in Label.Create(" ")
				  select q;
		bad.ViolationsOrEmpty.Should().ContainSingle(); // only the first
	}

	[Test]
	public void Combine_gathers_every_violation()
	{
		var combined = Result.Combine(Quantity.Create(0), Label.Create(" "), (q, l) => (q, l));

		combined.IsFail.Should().BeTrue();
		combined.ViolationsOrEmpty.Select(v => v.Message).Should().BeEquivalentTo("Quantity must be positive", "Label is blank");
		Result
			.Combine(Quantity.Create(2), Label.Create("a"), Label.Create("b"), (q, a, b) => q.Amount + a.Text + b.Text)
			.GetValueOrDefault("")
			.Should()
			.Be("2ab");
	}

	[Test]
	public void ConstrainedValue_starts_over_invalid_data_so_it_can_be_repaired_unless_Ensure_is_used_first()
	{
		var legacy = new Label(" ");
		var channel = ConstrainedValue.For(legacy).Enforce(Label.Rules);
		channel.Apply(l => l with { Text = "fixed" }).IsOk.Should().BeTrue();

		var strict = () => ConstrainedValue.For(Label.Rules.Ensure(legacy)).Enforce(Label.Rules);
		strict.Should().Throw<ConstraintViolationException>();
	}
}
