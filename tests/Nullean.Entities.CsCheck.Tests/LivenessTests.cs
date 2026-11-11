using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// A job a worker may finish at any time, while a heartbeat flips a bit forever. "Finish" is enabled in every state until the job is done.
internal sealed record Job(bool Beat, bool Done);

// A request that is pending, may be retried (and comes back to pending), and completes only from pending.
internal enum RequestState
{
	Pending,
	Retrying,
	Done
}

public class LivenessTests
{
	private static EntitySpec<Job> Jobs() =>
		ConstraintSet
			.From<Job>()
			.Spec(new Job(false, false))
			.Action("Beat", j => j with { Beat = !j.Beat })
			.Action("Finish", j => j with { Done = true })
			.Eventually("job-finishes", j => j.Done);

	private static EntitySpec<RequestState> Requests() =>
		EntitySpec.FromReducer(
			RequestState.Pending,
			(s, c) =>
				(s, c) switch
				{
					(RequestState.Pending, "Complete") => RequestState.Done,
					(RequestState.Pending, "Retry") => RequestState.Retrying,
					(RequestState.Retrying, "Back") => RequestState.Pending,
					_ => s
				},
			["Complete", "Retry", "Back"]
		).Eventually("request-completes", s => s == RequestState.Done);

	[Test]
	public void Without_fairness_a_loop_that_avoids_the_goal_fails()
	{
		var act = () => Jobs().Liveness();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Rule broken   job-finishes");
		message.Should().Contain("Fair actions  none (any loop counts)");
		message.Should().Contain("Then loops");
		message.Should().Contain("Beat");
	}

	[Test]
	public void Weak_fairness_rules_out_the_loop_when_the_fair_action_stays_enabled()
	{
		// Finish is enabled in both heartbeat states, so a fair run cannot beat forever without finishing.
		var report = Jobs().Fair("Finish").Liveness();

		report.States.Should().Be(4);
		report.Rules.Should().Equal("job-finishes");
		report.FairActions.Should().Equal("Finish");
	}

	[Test]
	public void Weak_fairness_does_not_help_an_action_that_is_only_enabled_now_and_then()
	{
		// Complete is enabled in Pending but not in Retrying, so the Pending <-> Retrying loop never keeps it enabled: weakly fair, and stuck.
		var act = () => Requests().Fair().Liveness();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Fair actions  every action");
		message.Should().Contain("Then loops");
		message.Should().Contain("Retry");
		message.Should().Contain("Back");
	}

	[Test]
	public void A_state_no_action_changes_is_a_stop_short_of_the_goal()
	{
		var spec = ConstraintSet
			.From<Job>()
			.Spec(new Job(false, false))
			.Action("Beat", j => j.Beat ? j : j with { Beat = true })
			.Eventually("job-finishes", j => j.Done);

		var act = () => spec.Liveness();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Then stops");
		message.Should().Contain("1 Beat");
	}

	[Test]
	public void LeadsTo_only_asks_from_the_states_where_its_condition_holds()
	{
		// Once the heartbeat is up, the job is finished: Finish only exists while beating, and the beat cannot drop.
		var spec = ConstraintSet
			.From<Job>()
			.Spec(new Job(false, false))
			.Action("Beat", j => j with { Beat = true })
			.Action("Finish", j => j.Beat ? j with { Done = true } : j)
			.LeadsTo("beating-leads-to-done", j => j.Beat, j => j.Done)
			.Fair("Finish");

		var report = spec.Liveness();

		report.States.Should().Be(3);
	}

	[Test]
	public void LeadsTo_whose_condition_never_holds_is_vacuous()
	{
		var spec = Jobs().LeadsTo("never", j => j.Beat && j.Done && !j.Beat, j => j.Done).Fair("Finish");

		var act = () => spec.Liveness();

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("'never'");
	}

	[Test]
	public void Exhaustive_proves_liveness_once_safety_holds()
	{
		var act = () => Jobs().Exhaustive();

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("Liveness failed.");
		Jobs().Fair("Finish").Exhaustive().Closed.Should().BeTrue();
	}

	[Test]
	public void Fairness_by_identity_treats_each_message_as_its_own_choice()
	{
		// Two messages in flight, delivered by position. Delivering a only moves it to the other position (a broker redelivering it), so the
		// run can deliver a forever. By position, that loop takes slot 0 and slot 1 in turn and looks fair while b starves; by the message itself,
		// b stays deliverable all the way round and is never taken, so the loop is not fair and every fair run delivers b.
		static EntitySpec<Bag> Spec() =>
			ConstraintSet
				.From<Bag>()
				.Spec(new Bag("ab", false))
				.Action(
					"Deliver",
					[0, 1],
					(bag, slot) => slot >= bag.InFlight.Length
						? bag
						: bag.InFlight[slot] == 'a'
							? bag with { InFlight = new string([.. bag.InFlight.Reverse()]) }
							: bag with { InFlight = bag.InFlight.Replace("b", "", StringComparison.Ordinal), GotB = true }
				)
				.Eventually("b-is-delivered", bag => bag.GotB);

		var byPosition = () => Spec().Fair("Deliver").Liveness();
		byPosition.Should().Throw<CsCheckException>().Which.Message.Should().Contain("Then loops");

		var byMessage = Spec().Fair("Deliver", (bag, slot) => (int)slot! < bag.InFlight.Length ? bag.InFlight[(int)slot!] : null).Liveness();
		byMessage.States.Should().Be(3);
	}

	internal sealed record Bag(string InFlight, bool GotB);
}
