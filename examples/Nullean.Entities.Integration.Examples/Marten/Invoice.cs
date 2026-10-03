// A Marten-style event-sourced aggregate: events are plain records, the aggregate has a static Create(event) and instance Apply(event) methods (Marten's
// conventions), and its rules are a ConstraintSet over the aggregate. The same Apply methods drive EntitySpec.EventSourced, so the aggregate's own
// code is what gets explored, not a copy of it.

namespace Nullean.Entities.Integration.Examples.Marten;

public sealed record InvoiceCreated(Guid Id, decimal Total);

public sealed record PaymentReceived(decimal Amount);

public sealed record InvoiceVoided;

public sealed record Invoice(Guid Id, decimal Total, decimal Paid, bool Voided)
{
	public static Invoice Create(InvoiceCreated e) => new(e.Id, e.Total, 0m, false);

	public Invoice Apply(PaymentReceived e) => this with { Paid = Paid + e.Amount };

	public Invoice Apply(InvoiceVoided _) => this with { Voided = true };

	/// <summary>Folds one event the way Marten's projection would.</summary>
	public Invoice Evolve(object e) => e switch
	{
		PaymentReceived paid => Apply(paid),
		InvoiceVoided voided => Apply(voided),
		_ => this,
	};
}

public abstract record InvoiceCommand
{
	public sealed record Pay(decimal Amount) : InvoiceCommand;

	public sealed record Void : InvoiceCommand;
}

public static class InvoiceDecider
{
	/// <summary>Decide: the events a command produces from the current state (none when it is refused).</summary>
	public static IEnumerable<object> Decide(Invoice invoice, InvoiceCommand command)
	{
		switch (command)
		{
			case InvoiceCommand.Pay pay when !invoice.Voided && pay.Amount > 0 && invoice.Paid + pay.Amount <= invoice.Total:
				yield return new PaymentReceived(pay.Amount);
				break;
			case InvoiceCommand.Void when !invoice.Voided && invoice.Paid == 0:
				yield return new InvoiceVoided();
				break;
		}
	}

	/// <summary>The same decide with the overpayment guard forgotten: what a review would miss and the exhaustive search finds.</summary>
	public static IEnumerable<object> DecideWithoutOverpaymentGuard(Invoice invoice, InvoiceCommand command)
	{
		if (command is InvoiceCommand.Pay pay && !invoice.Voided)
		{
			yield return new PaymentReceived(pay.Amount);
		}
		else if (command is InvoiceCommand.Void && !invoice.Voided && invoice.Paid == 0)
		{
			yield return new InvoiceVoided();
		}
	}
}

[Constraints]
public static partial class InvoiceConstraints
{
	public static readonly Constraint<Invoice> PaidIsNotNegative = Constraints<Invoice>.Always(i => i.Paid, paid => paid >= 0, "Paid is never negative.");

	public static readonly Constraint<Invoice> NotOverpaid = Constraints<Invoice>.Check(
		i => i.Paid <= i.Total ? [] : [Violation.Error("Total", "The invoice is paid beyond its total.")]
	);

	public static readonly Constraint<Invoice> PaymentsOnlyGrow = Constraints<Invoice>.Monotonic(i => i.Paid, MonotonicDirection.NonDecreasing);

	public static readonly Constraint<Invoice> VoidedStaysVoided = Constraints<Invoice>.Transitions(
		(before, after) => !before.Voided || after.Voided,
		"A voided invoice cannot be un-voided."
	);
}
