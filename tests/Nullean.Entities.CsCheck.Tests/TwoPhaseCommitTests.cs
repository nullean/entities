using System.Collections.Immutable;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// Two-phase commit: a coordinator asks every participant to prepare, each votes, and the coordinator commits only if all voted yes. Atomicity says
// no participant commits while another aborts; liveness says once a transaction begins, every participant learns the outcome.

internal enum Phase
{
	Idle,
	Waiting,
	Committed,
	Aborted
}

internal enum Outcome
{
	None,
	Commit,
	Abort
}

internal sealed record Prepare;

internal sealed record Vote(string Participant, bool Yes);

internal sealed record Decide(Outcome Outcome);

internal sealed record CoordinatorState(Phase Phase, ImmutableSortedSet<string> Yes);

internal sealed record ParticipantState(bool Prepared, bool Voted, Outcome Outcome);

internal sealed class TwoPhaseCommit
{
	public Node<CoordinatorState> Coordinator { get; } = SystemSpec.Node("coordinator", new CoordinatorState(Phase.Idle, []));

	public Node<ParticipantState>[] Participants { get; } =
	[
		SystemSpec.Node("p1", new ParticipantState(false, false, Outcome.None)),
		SystemSpec.Node("p2", new ParticipantState(false, false, Outcome.None))
	];

	/// <param name="commitOnFirstYes">The classic bug: the coordinator commits as soon as one participant says yes.</param>
	public TwoPhaseCommit(bool commitOnFirstYes = false)
	{
		Coordinator.Client("Begin", (c, send) =>
		{
			if (c.Phase != Phase.Idle)
			{
				return c;
			}
			foreach (var p in Participants)
			{
				send(p, new Prepare());
			}
			return c with { Phase = Phase.Waiting };
		}).Handles<Vote>((c, vote, send) =>
		{
			if (c.Phase != Phase.Waiting)
			{
				return c;
			}
			var yes = vote.Yes ? c.Yes.Add(vote.Participant) : c.Yes;
			var outcome = !vote.Yes ? Outcome.Abort : yes.Count == Participants.Length || commitOnFirstYes ? Outcome.Commit : Outcome.None;
			if (outcome == Outcome.None)
			{
				return c with { Yes = yes };
			}
			foreach (var p in Participants)
			{
				send(p, new Decide(outcome));
			}
			return c with { Yes = yes, Phase = outcome == Outcome.Commit ? Phase.Committed : Phase.Aborted };
		});

		foreach (var participant in Participants)
		{
			var me = participant;
			me
				.Handles<Prepare>((p, _, _) => p with { Prepared = true })
				.Handles<Decide>((p, d, _) => p with { Outcome = d.Outcome })
				.Client("Vote", [true, false], (p, yes, send) =>
				{
					if (!p.Prepared || p.Voted)
					{
						return p;
					}
					send(Coordinator, new Vote(me.Name, yes));
					return p with { Voted = true };
				});
		}
	}

	public ConstraintSet<SystemState> Rules =>
		ConstraintSet.From(
			("atomic", Constraints<SystemState>.Always(
				s => !(Participants.Any(p => p.In(s).Outcome == Outcome.Commit) && Participants.Any(p => p.In(s).Outcome == Outcome.Abort)),
				"No participant commits while another aborts."
			)),
			("commit-needs-every-yes", Constraints<SystemState>.Always(
				s => Coordinator.In(s).Phase != Phase.Committed || Coordinator.In(s).Yes.Count == Participants.Length,
				"The coordinator commits only when every participant voted yes."
			))
		);

	public SystemSpec System() => SystemSpec.Of([Coordinator, .. Participants]).Network(Network.Unordered).Bound(inFlight: 4);

	public bool Begun(SystemState s) => Coordinator.In(s).Phase != Phase.Idle;

	public bool EveryoneKnows(SystemState s) => Participants.All(p => p.In(s).Outcome != Outcome.None);
}

public class TwoPhaseCommitTests
{
	[Test]
	public void Two_phase_commit_is_atomic_in_every_interleaving()
	{
		var tpc = new TwoPhaseCommit();

		var report = tpc.System().Spec(tpc.Rules).Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().BeGreaterThan(20);
	}

	[Test]
	public void Committing_on_the_first_yes_breaks_atomicity()
	{
		var tpc = new TwoPhaseCommit(commitOnFirstYes: true);

		var act = () => tpc.System().Spec(tpc.Rules).Exhaustive();

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("commit-needs-every-yes");
	}

	[Test]
	public void Every_participant_learns_the_outcome_when_delivery_is_fair()
	{
		var tpc = new TwoPhaseCommit();

		var report = tpc.System().Spec(tpc.Rules).LeadsTo("everyone-learns-the-outcome", tpc.Begun, tpc.EveryoneKnows).Liveness();

		// Voting needs no fairness: a run only stops where nothing can change the state, so a participant that can still vote will.
		// Fairness is for loops, and voting has none.
		report.FairActions.Should().Equal("Deliver");
	}

	[Test]
	public void A_coordinator_that_restarts_without_its_votes_blocks_the_transaction()
	{
		// The coordinator logs its phase but keeps the votes in memory. Crash it after the votes arrive: it comes back waiting, the votes are gone,
		// nobody sends them again, and the participants wait for an outcome for ever. That is 2PC's blocking problem, and the reason its log matters.
		var tpc = new TwoPhaseCommit();

		var act =
			() => tpc
				.System()
				.Crashes(tpc.Coordinator, durable: c => c with { Yes = [] })
				.Spec(tpc.Rules)
				.LeadsTo("everyone-learns-the-outcome", tpc.Begun, tpc.EveryoneKnows)
				.Liveness();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Rule broken   everyone-learns-the-outcome");
		message.Should().Contain("crash coordinator");
		message.Should().Contain("Then stops");
	}

	[Test]
	public void A_coordinator_that_logs_its_votes_recovers()
	{
		var tpc = new TwoPhaseCommit();

		var report = tpc
			.System()
			.Crashes(tpc.Coordinator, durable: c => c)
			.Spec(tpc.Rules)
			.LeadsTo("everyone-learns-the-outcome", tpc.Begun, tpc.EveryoneKnows)
			.Liveness();

		report.States.Should().BeGreaterThan(20);
	}
}
