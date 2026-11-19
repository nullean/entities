using AwesomeAssertions;

namespace Nullean.Entities.Tests;

public class PerValueMachineTests
{
	private enum Kind
	{
		Card,
		Invoice
	}

	private enum Phase
	{
		New,
		Authorised,
		Captured,
		Sent,
		Paid,
		Overdue
	}

	private sealed record Payment(Kind Kind, Phase Phase);

	private static readonly StateMachine<Phase, string> CardMachine = StateMachine
		.Define<Phase, string>(Phase.New)
		.Permit("Authorise", Phase.New, Phase.Authorised)
		.Permit("Capture", Phase.Authorised, Phase.Captured)
		.Build();

	private static readonly StateMachine<Phase, string> InvoiceMachine = StateMachine
		.Define<Phase, string>(Phase.New)
		.Permit("Send", Phase.New, Phase.Sent)
		.Permit("Pay", Phase.Sent, Phase.Paid)
		.Permit("Lapse", Phase.Sent, Phase.Overdue)
		.Permit("PayLate", Phase.Overdue, Phase.Paid)
		.Build();

	private static StateMachine<Phase, string> MachineFor(Payment p) => p.Kind == Kind.Card ? CardMachine : InvoiceMachine;

	private static readonly Constraint<Payment> Lifecycle = Constraints<Payment>.Transitions(MachineFor, p => p.Phase, CardMachine, InvoiceMachine);

	[Test]
	public void Each_value_follows_the_machine_picked_for_it()
	{
		Lifecycle.Check(new(Kind.Card, Phase.New), new(Kind.Card, Phase.Authorised)).Should().BeEmpty();
		Lifecycle.Check(new(Kind.Invoice, Phase.New), new(Kind.Invoice, Phase.Sent)).Should().BeEmpty();
	}

	[Test]
	public void An_edge_of_the_other_machine_is_rejected()
	{
		Lifecycle.Check(new(Kind.Card, Phase.New), new(Kind.Card, Phase.Sent)).Should().NotBeEmpty();
		Lifecycle.Check(new(Kind.Invoice, Phase.New), new(Kind.Invoice, Phase.Authorised)).Should().NotBeEmpty();
	}

	[Test]
	public void Staying_put_is_not_a_transition()
	{
		Lifecycle.Check(new(Kind.Card, Phase.Captured), new(Kind.Card, Phase.Captured)).Should().BeEmpty();
	}

	[Test]
	public void Moving_to_another_machine_is_a_violation()
	{
		Lifecycle.Check(new(Kind.Card, Phase.New), new(Kind.Invoice, Phase.New)).Should().ContainSingle().Which.Message.Should().Contain("another lifecycle");
	}

	[Test]
	public void It_is_classified_like_Allow_and_requires_every_edge_of_every_machine()
	{
		Lifecycle.Axis.Should().Be(Axis.Invariant);
		Lifecycle.StateOf!(new(Kind.Card, Phase.Paid)).Should().Be("Paid");
		Lifecycle
			.Edges
			.Should()
			.BeEquivalentTo([("New", "Authorised"), ("Authorised", "Captured"), ("New", "Sent"), ("Sent", "Paid"), ("Sent", "Overdue"), ("Overdue", "Paid")]);
	}

	[Test]
	public void A_machine_that_was_not_declared_is_an_error()
	{
		var rule = Constraints<Payment>.Transitions(MachineFor, p => p.Phase, CardMachine);
		var act = () => rule.Check(new(Kind.Invoice, Phase.New), new(Kind.Invoice, Phase.Sent));
		act.Should().Throw<InvalidOperationException>();
	}
}
