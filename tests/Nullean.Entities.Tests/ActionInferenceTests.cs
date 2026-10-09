using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal sealed record InferOutcome<T>(T? Value, IReadOnlyList<string> Notes);

internal sealed record InferPair<T>(T Before, T After);

internal sealed record InferHeld<T>(T Entity, T Spare);

internal sealed record InferTicket(string Status, int Version)
{
	public InferTicket Touch() => this with { Version = Version + 1 };

	public InferOutcome<InferTicket> Close() => new(this with { Status = "closed" }, []);

	public InferOutcome<InferTicket> Reopen(string reason) => new(this with { Status = reason }, []);

	public (InferTicket Ticket, int Changes) Archive() => (this with { Status = "archived" }, 1);

	public InferHeld<InferTicket> Hold() => new(this with { Status = "held" }, this);

	[ConstraintAction(nameof(InferPair<>.After))]
	public InferPair<InferTicket> Fork() => new(this, this with { Status = "forked" });

	// Not actions: asynchronous, a Try-pattern, an ambiguous wrapper, and a wrapper of something else.
	public Task<InferTicket> Sync() => Task.FromResult(this);

	public bool TryMerge(out InferTicket merged)
	{
		merged = this;
		return true;
	}

	public InferPair<InferTicket> Split() => new(this, this);

	public string Label() => Status;

	public InferOutcome<string> Describe() => new(Status, []);
}

[Constraints]
internal static partial class InferTicketConstraints
{
	public static readonly Constraint<InferTicket> HasStatus = Constraints<InferTicket>.Always(t => t.Status.Length > 0, "A ticket has a status.");
}

public class ActionInferenceTests
{
	private static readonly InferTicket Open = new("open", 1);

	[Test]
	public void The_entity_and_the_wrappers_it_can_be_read_from_become_actions_in_declaration_order() =>
		InferTicketConstraints.Actions.Select(a => a.Name).Should().Equal("Touch", "Close", "Reopen", "Archive", "Hold", "Fork");

	[Test]
	public void An_action_that_returns_a_wrapper_reads_the_new_entity_out_of_it()
	{
		Apply("Touch").Version.Should().Be(2);
		Apply("Close").Status.Should().Be("closed");
		Apply("Reopen", "waiting").Status.Should().Be("waiting");
		Apply("Archive").Status.Should().Be("archived");
	}

	[Test]
	public void With_several_members_of_the_entity_type_the_conventional_name_wins_and_the_attribute_overrides()
	{
		Apply("Hold").Status.Should().Be("held");
		Apply("Fork").Status.Should().Be("forked");
	}

	[Test]
	public void A_one_argument_action_declares_the_type_of_its_argument() =>
		InferTicketConstraints.Actions.Single(a => a.Name == "Reopen").ArgumentType.Should().Be(typeof(string));

	[Test]
	public void An_action_runs_through_the_set_like_any_other()
	{
		var outcome = ConstrainedValue
			.For(Open)
			.Enforce(InferTicketConstraints.All)
			.Apply(t => InferTicketConstraints.Actions.Single(a => a.Name == "Close").Apply(t, null));

		outcome.Should().BeOfType<Result<InferTicket>.Ok>();
	}

	private static InferTicket Apply(string name, object? argument = null) => InferTicketConstraints.Actions.Single(a => a.Name == name).Apply(Open, argument);
}
