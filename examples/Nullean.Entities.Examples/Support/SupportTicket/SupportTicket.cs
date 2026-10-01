namespace Nullean.Entities.Examples.Support.SupportTicket;

public enum TicketStatus
{
	Open,
	InProgress,
	AwaitingCustomer,
	Resolved,
	Closed
}

public enum Priority
{
	Low,
	Medium,
	High,
	Critical
}

public sealed record TicketComment(Guid Id, string AuthorId, string Body, DateTimeOffset PostedAt);

public sealed record SupportTicket(
	Guid Id,
	string CustomerId,
	string Title,
	Priority Priority,
	TicketStatus Status,
	string? AssigneeId,
	IReadOnlyList<TicketComment> Comments,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ResolvedAt
)
{
	public SupportTicket Assign(string agentId) => this with { Status = TicketStatus.InProgress, AssigneeId = agentId };

	public SupportTicket Escalate() => this with { Priority = Priority.Critical };

	public SupportTicket AddComment(TicketComment c) => this with { Comments = [.. Comments, c] };

	public SupportTicket Resolve(DateTimeOffset at) => this with { Status = TicketStatus.Resolved, ResolvedAt = at };

	public SupportTicket Close() => this with { Status = TicketStatus.Closed };

	public SupportTicket Reopen() => this with { Status = TicketStatus.InProgress, ResolvedAt = null };
}

[Constraints]
public static partial class SupportTicketConstraints
{
	public static readonly Constraint<SupportTicket> LegalStatusTransitions = Constraints<SupportTicket>
		.Transitions(t => t.Status)
		.Allow(TicketStatus.Open, TicketStatus.InProgress)
		.Allow(TicketStatus.InProgress, TicketStatus.AwaitingCustomer)
		.Allow(TicketStatus.InProgress, TicketStatus.Resolved)
		.Allow(TicketStatus.AwaitingCustomer, TicketStatus.InProgress)
		.Allow(TicketStatus.AwaitingCustomer, TicketStatus.Resolved)
		.Allow(TicketStatus.Resolved, TicketStatus.Closed)
		.Allow(TicketStatus.Resolved, TicketStatus.InProgress);

	public static readonly Constraint<SupportTicket> InProgressRequiresAssignee = Constraints<SupportTicket>
		.When(t => t.Status == TicketStatus.InProgress)
		.Requires(t => t.AssigneeId is not null, "In-progress tickets must be assigned to an agent.");

	public static readonly Constraint<SupportTicket> ResolvedRequiresTimestamp = Constraints<SupportTicket>
		.When(t => t.Status is TicketStatus.Resolved or TicketStatus.Closed)
		.Requires(t => t.ResolvedAt is not null, "Resolved/closed tickets must have a resolution timestamp.");

	public static readonly Constraint<SupportTicket> TitleNotEmpty = Constraints<SupportTicket>.Always(
		t => !string.IsNullOrWhiteSpace(t.Title),
		"Title must not be empty."
	);
}
