using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Support.SupportTicket;

namespace Nullean.Entities.Examples.Tests.Support;

public class SupportTicketTests
{
	private static readonly ConstraintSet<SupportTicket> Constraints = SupportTicketConstraints.All;

	private static SupportTicket Open() =>
		new(Guid.NewGuid(), "cust-1", "Issue with login", Priority.Medium, TicketStatus.Open, null, [], DateTimeOffset.UtcNow, null);

	[Test]
	public void Validate_open_ticket_passes()
	{
		Constraints.Validate(Open()).Should().BeEmpty();
	}

	[Test]
	public void Validate_empty_title_fails()
	{
		var bad = Open() with { Title = "" };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_in_progress_without_assignee_fails()
	{
		var bad = Open() with { Status = TicketStatus.InProgress, AssigneeId = null };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_assign()
	{
		var channel = ConstrainedValue.For(Open()).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t.Assign("agent-1"));
		result.Should().BeOfType<ApplyOutcome<SupportTicket>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Open_to_Resolved_transition()
	{
		var channel = ConstrainedValue.For(Open()).Enforce(Constraints);
		var resolved = Open() with { Status = TicketStatus.Resolved, ResolvedAt = DateTimeOffset.UtcNow };
		var result = channel.ApplyDetailed(_ => resolved);
		result.Should().BeOfType<ApplyOutcome<SupportTicket>.Failure>();
	}

	[Test]
	public void Channel_Noop_when_no_mutation()
	{
		var channel = ConstrainedValue.For(Open()).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t);
		result.Should().BeOfType<ApplyOutcome<SupportTicket>.Noop>();
	}

	// AwaitingCustomer is a legal state but no action produces it, so those edges are never taken: requireCoverage is
	// off until the entity grows an action for it (see AUTOTEST_NOTES.md).
	[Test]
	public void AutoTest_enforced_tickets_only_follow_legal_transitions() =>
		SupportTicketConstraints.Instance.Spec(Open()).Enforced().Exhaustive(requireCoverage: false);
}
