// Declaring the lifecycle as a machine finds, at declaration time, the states the entity can never reach (AUTOTEST_NOTES.md section 1).

using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.HR.OrgChart;
using Nullean.Entities.Examples.Support.SupportTicket;

namespace Nullean.Entities.Examples.Tests.AutoTests;

public class DeclaredMachineExamples
{
	private static SupportTicket Open() =>
		new(Guid.NewGuid(), "cust-1", "Issue with login", Priority.Medium, TicketStatus.Open, null, [], DateTimeOffset.UnixEpoch, null);

	private static Person ActivePerson() => new("P1", "Person P1", null, EmploymentStatus.Active, 1, []);

	// ── The machine is self-consistent, and is the same as the hand-written allow-list ───────────────

	[Test]
	public void The_support_ticket_machine_reaches_every_state_and_takes_every_edge() =>
		SupportTicketMachine.Machine.Spec().Exhaustive().Closed.Should().BeTrue();

	[Test]
	public void The_machine_as_a_constraint_agrees_with_the_hand_written_one_on_every_pair_of_statuses()
	{
		var fromMachine = ConstraintSet.From(("Machine", SupportTicketMachine.Machine.AsConstraint<SupportTicket>(t => t.Status)));
		var states = Enum.GetValues<TicketStatus>();

		foreach (var from in states)
		{
			foreach (var to in states)
			{
				var start = Open() with { Status = from };
				var machine = fromMachine.Apply(start, t => t with { Status = to }).IsOk;
				var handWritten = ConstraintSet.From(("Hand", SupportTicketConstraints.LegalStatusTransitions)).Apply(start, t => t with { Status = to }).IsOk;

				machine.Should().Be(handWritten, $"{from} -> {to}");
			}
		}
	}

	[Test]
	public void The_diagram_is_generated_not_drawn() =>
		SupportTicketMachine.Machine.ToMermaid().Should().Contain("InProgress --> AwaitingCustomer : AwaitCustomer").And.Contain("Resolved --> Closed : Close");

	// ── The entity against the machine: the finding ──────────────────────────────────────────────────

	[Test]
	public void A_ticket_cannot_do_what_the_machine_says_so_AwaitingCustomer_is_unreachable_in_practice()
	{
		var act = () =>
			SupportTicketMachine.Machine.ConformTo(
				Open(),
				t => t.Status,
				bindings: [
					("Assign", t => t.Assign("agent-1")),
					("Resolve", t => t.Resolve(DateTimeOffset.UnixEpoch)),
					("Close", t => t.Close()),
					("Reopen", t => t.Reopen())
				]
			);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("AwaitCustomer").And.Contain("CustomerReplied");
	}

	[Test]
	public void Even_the_part_the_entity_does_implement_diverges_because_Resolve_is_unguarded()
	{
		var implemented = StateMachine
			.Define<TicketStatus, string>(TicketStatus.Open)
			.Permit("Assign", TicketStatus.Open, TicketStatus.InProgress)
			.Permit("Resolve", TicketStatus.InProgress, TicketStatus.Resolved)
			.Permit("Close", TicketStatus.Resolved, TicketStatus.Closed)
			.Permit("Reopen", TicketStatus.Resolved, TicketStatus.InProgress)
			.Build();

		var act = () =>
			implemented.ConformTo(
				Open(),
				t => t.Status,
				iter: 200,
				bindings: [
					("Assign", t => t.Assign("agent-1")),
					("Resolve", t => t.Resolve(DateTimeOffset.UnixEpoch)),
					("Close", t => t.Close()),
					("Reopen", t => t.Reopen())
				]
			);

		// the machine forbids these on an Open ticket (they do nothing), but the methods change the status regardless: the rule lives only in Apply.
		// Resolve, Close and Reopen are all unguarded, and which one a random walk meets first (they all diverge in one step) is not fixed.
		act
			.Should()
			.Throw<Exception>()
			.Which
			.Message
			.Should()
			.MatchRegex("(Resolve|Close|Reopen)")
			.And
			.MatchRegex("the machine says Open, but the entity is in (Resolved|Closed|InProgress)");
	}

	[Test]
	public void A_person_cannot_go_on_leave_because_the_entity_has_no_way_to()
	{
		var act = () => PersonMachine.Machine.ConformTo(ActivePerson(), p => p.Status, bindings: [("Terminate", p => p.Terminate())]);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("GoOnLeave").And.Contain("Return");
	}

	[Test]
	public void The_person_machine_is_self_consistent() => PersonMachine.Machine.Spec().Exhaustive().States.Should().Be(3);
}
