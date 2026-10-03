using Nullean.Entities;

namespace SupportDesk;

public enum Priority
{
	P1,
	P2,
	P3,
}

public enum TicketStatus
{
	New,
	Acknowledged,
	InProgress,
	Resolved,
	Closed,
}

public sealed record Ticket(Guid Id, string Subject, Priority Priority, TicketStatus Status, DateTimeOffset StatusSince);

/// <summary>What happened to a ticket. The service appends these to the log; the SLA rules read them.</summary>
public abstract record TicketEvent(Guid TicketId)
{
	public sealed record Opened(Guid TicketId, Priority Priority) : TicketEvent(TicketId);

	public sealed record Acknowledged(Guid TicketId) : TicketEvent(TicketId);

	public sealed record Resolved(Guid TicketId) : TicketEvent(TicketId);

	public sealed record Reopened(Guid TicketId) : TicketEvent(TicketId);

	public sealed record Closed(Guid TicketId) : TicketEvent(TicketId);
}

/// <summary>
/// The lifecycle, declared as data. States are plain values, so this machine can be drawn, read back from a diagram and checked against the service.
/// <c>After</c> is a timeout: a ticket that has sat Resolved for three days closes by itself.
/// </summary>
public static class TicketLifecycle
{
	public static readonly StateMachine<TicketStatus, string> Machine = StateMachine
		.Define<TicketStatus, string>(TicketStatus.New)
		.Permit("Acknowledge", TicketStatus.New, TicketStatus.Acknowledged)
		.Permit("Start", TicketStatus.Acknowledged, TicketStatus.InProgress)
		.Permit("Resolve", TicketStatus.InProgress, TicketStatus.Resolved)
		.Permit("Reopen", TicketStatus.Resolved, TicketStatus.InProgress)
		.Permit("Close", TicketStatus.Resolved, TicketStatus.Closed)
		.After(TimeSpan.FromDays(3), TicketStatus.Resolved, TicketStatus.Closed)
		.Build();
}

/// <summary>Rules about a ticket at a point in time, and about each change to it. The machine contributes the rule about changes.</summary>
public static class TicketRules
{
	public static readonly ConstraintSet<Ticket> All = ConstraintSet.From(
		("lifecycle", TicketLifecycle.Machine.AsConstraint<Ticket>(t => t.Status)),
		("subject-is-required", Constraints<Ticket>.Always(t => !string.IsNullOrWhiteSpace(t.Subject), "A ticket needs a subject"))
	);
}

/// <summary>
/// Rules about <em>sequences in time</em>, which no single ticket can express: after a P1 is opened it must be acknowledged within the hour, and no ticket closes twice.
/// The same set watches the live stream and audits a recorded one.
/// </summary>
public static class SlaRules
{
	public static readonly TimeSpan P1Window = TimeSpan.FromHours(1);

	public static readonly TemporalRuleSet<TicketEvent> All = TemporalRuleSet<TicketEvent>.From(
		("p1-acknowledged-within-1h",
		Temporal<TicketEvent>.AfterUntil(
			e => e is TicketEvent.Opened { Priority: Priority.P1 },
			(opened, next) => next is TicketEvent.Acknowledged && next.TicketId == opened.TicketId,
			P1Window
		)),
		("a-ticket-closes-once", Temporal<TicketEvent>.NeverTwice<TicketEvent.Closed>(c => c.TicketId))
	);
}
