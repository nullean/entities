// The support-ticket lifecycle declared as data. The picture in the docs, the allow-list constraint and the test all come from this one value
// (see SupportTicketConstraints.LegalStatusTransitions for the hand-written list it replaces).

namespace Nullean.Entities.Examples.Support.SupportTicket;

public static class SupportTicketMachine
{
	public static readonly StateMachine<TicketStatus, string> Machine = StateMachine
		.Define<TicketStatus, string>(TicketStatus.Open)
		.Permit("Assign", TicketStatus.Open, TicketStatus.InProgress)
		.Permit("AwaitCustomer", TicketStatus.InProgress, TicketStatus.AwaitingCustomer)
		.Permit("CustomerReplied", TicketStatus.AwaitingCustomer, TicketStatus.InProgress)
		.Permit("Resolve", [TicketStatus.InProgress, TicketStatus.AwaitingCustomer], TicketStatus.Resolved)
		.Permit("Close", TicketStatus.Resolved, TicketStatus.Closed)
		.Permit("Reopen", TicketStatus.Resolved, TicketStatus.InProgress)
		.Build();
}
