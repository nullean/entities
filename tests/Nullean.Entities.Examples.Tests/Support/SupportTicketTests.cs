using AwesomeAssertions;
using Nullean.Entities.Examples.Support.SupportTicket;

namespace Nullean.Entities.Examples.Tests.Support;

public class SupportTicketTests
{
	private static readonly ConstraintSet<SupportTicket> Constraints = SupportTicketConstraints.Constraints;

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
		var channel = ConstraintChannel.For(Open()).Enforce(Constraints);
		var result = channel.Apply(t => t.Assign("agent-1"));
		result.Should().BeOfType<ChannelResult<SupportTicket>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Open_to_Resolved_transition()
	{
		var channel = ConstraintChannel.For(Open()).Enforce(Constraints);
		var resolved = Open() with { Status = TicketStatus.Resolved, ResolvedAt = DateTimeOffset.UtcNow };
		var result = channel.Apply(_ => resolved);
		result.Should().BeOfType<ChannelResult<SupportTicket>.Failure>();
	}

	[Test]
	public void Channel_Noop_when_no_mutation()
	{
		var channel = ConstraintChannel.For(Open()).Enforce(Constraints);
		var result = channel.Apply(t => t);
		result.Should().BeOfType<ChannelResult<SupportTicket>.Noop>();
	}
}
