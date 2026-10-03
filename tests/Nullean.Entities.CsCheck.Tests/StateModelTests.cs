using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// A mutable door, to show the same machine conforming a class that changes in place.
internal sealed class MutableDoor
{
	public DoorState State { get; private set; } = DoorState.Closed;

	public bool LockIsBroken { get; init; }

	public void Open()
	{
		if (State == DoorState.Closed)
		{
			State = DoorState.Open;
		}
	}

	public void Close()
	{
		if (State == DoorState.Open)
		{
			State = DoorState.Closed;
		}
	}

	public void Lock()
	{
		if (State == DoorState.Closed || (LockIsBroken && State == DoorState.Open))
		{
			State = DoorState.Locked;
		}
	}

	public void Unlock()
	{
		if (State == DoorState.Locked)
		{
			State = DoorState.Closed;
		}
	}

	public override string ToString() => State.ToString();
}

public class DeclaredMachineTests
{
	private static readonly StateMachine<DoorState, string> Machine = StateMachine
		.Define<DoorState, string>(DoorState.Closed)
		.Permit("Open", DoorState.Closed, DoorState.Open)
		.Permit("Close", DoorState.Open, DoorState.Closed)
		.Permit("Lock", DoorState.Closed, DoorState.Locked)
		.Permit("Unlock", DoorState.Locked, DoorState.Closed)
		.Build();

	// ── The machine as data ──────────────────────────────────────────────────

	[Test]
	public void Fire_follows_the_declared_edges_and_a_forbidden_trigger_does_nothing()
	{
		Machine.Fire(DoorState.Closed, "Open").Should().Be(DoorState.Open);
		Machine.TryFire(DoorState.Locked, "Open", out var stays).Should().BeFalse();
		stays.Should().Be(DoorState.Locked);
		Machine.Permitted(DoorState.Closed).Should().BeEquivalentTo("Open", "Lock");
		var act = () => Machine.Fire(DoorState.Locked, "Open");
		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("not permitted");
	}

	[Test]
	public void A_trigger_leads_to_one_state_from_a_given_state()
	{
		var act = () =>
			StateMachine
				.Define<DoorState, string>(DoorState.Closed)
				.Permit("Open", DoorState.Closed, DoorState.Open)
				.Permit("Open", DoorState.Closed, DoorState.Locked);

		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	public void AsConstraint_is_the_allow_list_so_it_never_drifts_from_the_diagram()
	{
		var rule = ConstraintSet.From(("Declared", Machine.AsConstraint<Door>(d => d.State)));

		rule.Apply(Door.New(), d => d.Open()).IsOk.Should().BeTrue();
		rule.Apply(new Door(DoorState.Locked), d => d with { State = DoorState.Open }).IsFail.Should().BeTrue();
		rule.Apply(Door.New(), d => d).IsOk.Should().BeTrue(); // staying put is not a transition
	}

	// ── Exploring the machine itself ─────────────────────────────────────────

	[Test]
	public void Spec_proves_every_state_reachable_and_every_edge_taken()
	{
		var report = Machine.Spec().Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().Be(3);
	}

	[Test]
	public void A_declared_state_nothing_leads_to_is_found_at_declaration_time()
	{
		var machine = StateMachine
			.Define<DoorState, string>(DoorState.Closed)
			.Permit("Open", DoorState.Closed, DoorState.Open)
			.Permit("Close", DoorState.Open, DoorState.Closed)
			.State(DoorState.Locked) // declared, but no trigger reaches it

			.Build();

		var act = () => machine.Spec().Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("Locked");
	}

	[Test]
	public void Timeouts_are_explored_as_moves_that_happen_by_themselves()
	{
		var machine = StateMachine
			.Define<DoorState, string>(DoorState.Closed)
			.Permit("Open", DoorState.Closed, DoorState.Open)
			.After(TimeSpan.FromSeconds(30), DoorState.Open, DoorState.Closed)
			.Build();

		machine.Spec().Exhaustive().States.Should().Be(2);
		machine.ToMermaid().Should().Contain("Open --> Closed : after 30s");
	}

	// ── Conformance: the machine as the specification of a real entity ───────

	[Test]
	public void A_faithful_immutable_entity_conforms_to_the_machine() =>
		Machine
			.ConformTo(
				Door.New(),
				d => d.State,
				iter: 100,
				bindings: [("Open", d => d.Open()), ("Close", d => d.Close()), ("Lock", d => d.Lock()), ("Unlock", d => d.Unlock())]
			)
			.Should()
			.NotBeNull();

	[Test]
	public void A_faithful_mutable_entity_conforms_to_the_machine() =>
		Machine
			.ConformToMutable(
				() => new MutableDoor(),
				d => d.State,
				iter: 100,
				bindings: [("Open", d => d.Open()), ("Close", d => d.Close()), ("Lock", d => d.Lock()), ("Unlock", d => d.Unlock())]
			)
			.Should()
			.NotBeNull();

	[Test]
	public void An_entity_that_does_something_the_machine_forbids_is_caught()
	{
		// the lock "works" on an open door: the machine only allows locking a closed one
		var act = () =>
			Machine.ConformToMutable(
				() => new MutableDoor { LockIsBroken = true },
				d => d.State,
				iter: 200,
				bindings: [("Open", d => d.Open()), ("Close", d => d.Close()), ("Lock", d => d.Lock()), ("Unlock", d => d.Unlock())]
			);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("the machine says").And.Contain("Lock");
	}

	[Test]
	public void A_trigger_the_entity_cannot_perform_is_reported_before_any_walk()
	{
		var withKick = StateMachine
			.Define<DoorState, string>(DoorState.Closed)
			.Permit("Open", DoorState.Closed, DoorState.Open)
			.Permit("Kick", DoorState.Closed, DoorState.Open)
			.Build();

		var act = () => withKick.ConformTo(Door.New(), d => d.State, bindings: [("Open", d => d.Open())]);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Kick");
	}

	[Test]
	public void An_entity_that_starts_in_another_state_than_the_machine_is_rejected()
	{
		var act = () =>
			Machine.ConformTo(
				new Door(DoorState.Locked),
				d => d.State,
				bindings: [("Open", d => d.Open()), ("Close", d => d.Close()), ("Lock", d => d.Lock()), ("Unlock", d => d.Unlock())]
			);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("starts in");
	}

	// ── Diagrams ─────────────────────────────────────────────────────────────

	[Test]
	public void The_machine_is_a_mermaid_diagram_and_a_diagram_is_a_machine()
	{
		var diagram = Machine.ToMermaid();

		diagram.Should().StartWith("stateDiagram-v2").And.Contain("[*] --> Closed").And.Contain("Closed --> Open : Open");

		var read = StateMachine.FromMermaid(diagram);

		read.Initial.Should().Be("Closed");
		read
			.Transitions
			.Select(t => (t.From, t.Trigger, t.To))
			.Should()
			.BeEquivalentTo(Machine.Transitions.Select(t => (t.From.ToString(), t.Trigger, t.To.ToString())));
	}

	[Test]
	public void A_diagram_written_by_hand_with_timeouts_and_a_final_state_is_read()
	{
		var machine = StateMachine.FromMermaid(
			"""
			stateDiagram-v2
			    %% a trial subscription
			    [*] --> Trial
			    Trial --> Active : Activate
			    Trial --> Expired : after 14d
			    Active --> Cancelled : Cancel
			    Cancelled --> [*]
			"""
		);

		machine.Initial.Should().Be("Trial");
		machine.Triggers.Should().BeEquivalentTo("Activate", "Cancel");
		machine.Timeouts.Should().ContainSingle(t => t.From == "Trial" && t.To == "Expired" && t.After == TimeSpan.FromDays(14));
		machine.Spec().Exhaustive().States.Should().Be(4);
	}

	[Test]
	public void A_line_that_is_not_a_transition_is_a_format_error_that_names_it()
	{
		var act = () => StateMachine.FromMermaid("stateDiagram-v2\n  A ==> B");

		act.Should().Throw<FormatException>().Which.Message.Should().Contain("A ==> B");
	}
}

public class OtherModelTests
{
	// ── Reducer ──────────────────────────────────────────────────────────────

	[Test]
	public void A_reducer_is_a_state_machine_and_exhaustive_finds_a_command_that_breaks_a_rule()
	{
		var nonNegative = ConstraintSet.From(("NonNegative", Constraints<int>.Always(n => n >= 0, "count must not go below zero")));
		var spec = EntitySpec.FromReducer(0, (int n, string command) => command == "inc" ? n + 1 : n - 1, ["inc", "dec"], nonNegative).Boundary(n => n < 5);

		var act = () => spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("NonNegative");
	}

	[Test]
	public void A_reducer_that_guards_itself_is_proved_on_a_bounded_space()
	{
		var nonNegative = ConstraintSet.From(("NonNegative", Constraints<int>.Always(n => n >= 0, "count must not go below zero")));

		var report = EntitySpec
			.FromReducer(0, (int n, string command) => command == "inc" ? n + 1 : Math.Max(0, n - 1), ["inc", "dec"], nonNegative)
			.Boundary(n => n < 4)
			.Exhaustive();

		report.Closed.Should().BeTrue();
	}

	// ── Event-sourced (Decide / Evolve) ──────────────────────────────────────

	private enum DoorCommand
	{
		Open,
		Close,
		Lock,
		Unlock
	}

	private abstract record DoorEvent
	{
		public sealed record Opened : DoorEvent;

		public sealed record Closed : DoorEvent;

		public sealed record Locked : DoorEvent;

		public sealed record Unlocked : DoorEvent;
	}

	private static IEnumerable<DoorEvent> Decide(DoorState state, DoorCommand command, bool buggyOpen)
	{
		switch (command)
		{
			case DoorCommand.Open when state == DoorState.Closed || (buggyOpen && state == DoorState.Locked):
				yield return new DoorEvent.Opened();
				break;
			case DoorCommand.Close when state == DoorState.Open:
				yield return new DoorEvent.Closed();
				break;
			case DoorCommand.Lock when state == DoorState.Closed:
				yield return new DoorEvent.Locked();
				break;
			case DoorCommand.Unlock when state == DoorState.Locked:
				yield return new DoorEvent.Unlocked();
				break;
		}
	}

	private static DoorState Evolve(DoorState state, DoorEvent e) => e switch
	{
		DoorEvent.Opened => DoorState.Open,
		DoorEvent.Closed => DoorState.Closed,
		DoorEvent.Locked => DoorState.Locked,
		DoorEvent.Unlocked => DoorState.Closed,
		_ => state,
	};

	private static EventSourcedSpec<DoorState, DoorCommand, DoorEvent> Doors(bool buggyOpen) =>
		EntitySpec.EventSourced<DoorState, DoorCommand, DoorEvent>(
			DoorState.Closed,
			(state, command) => Decide(state, command, buggyOpen),
			Evolve,
			[DoorCommand.Open, DoorCommand.Close, DoorCommand.Lock, DoorCommand.Unlock],
			DoorConstraints.All.Map<DoorState>(state => new Door(state))
		);

	// A door must be unlocked before it is opened: Locked must never be directly followed by Opened. Transitions looks at raw
	// adjacent events; Never<Locked, Opened> would ignore an Unlocked in between and report a false violation.
	private static readonly TemporalRuleSet<DoorEvent> NoOpenWhileLocked = TemporalRuleSet<DoorEvent>.From(
		("NeverOpenedAfterLocked", Temporal<DoorEvent>.Transitions((previous, next) => !(previous is DoorEvent.Locked && next is DoorEvent.Opened)))
	);

	[Test]
	public void An_event_sourced_model_is_explored_like_any_other()
	{
		var report = Doors(buggyOpen: false).Spec.Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().Be(3);
	}

	[Test]
	public void Temporal_rules_over_the_emitted_events_pass_for_a_correct_decide() =>
		Doors(buggyOpen: false).SampleEvents(NoOpenWhileLocked, iter: 200);

	[Test]
	public void Temporal_rules_over_the_emitted_events_catch_a_decide_that_opens_a_locked_door()
	{
		var act = () => Doors(buggyOpen: true).SampleEvents(NoOpenWhileLocked, iter: 400);

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("NeverOpenedAfterLocked");
	}

	[Test]
	public void The_state_constraints_still_catch_the_same_bug_without_any_temporal_rule()
	{
		// Locked -> Open is not an allowed transition of DoorConstraints, so the folded state shows it too
		var act = () => Doors(buggyOpen: true).Spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("LegalTransitions");
	}

	// ── Mutable + replay ─────────────────────────────────────────────────────

	[Test]
	public void A_mutable_class_is_explored_by_replaying_its_actions()
	{
		var spec = EntitySpec
			.ForMutable(() => new MutableDoor(), DoorConstraints.All.Map<MutableDoor>(d => new Door(d.State)))
			.Do("Open", d => d.Open())
			.Do("Close", d => d.Close())
			.Do("Lock", d => d.Lock())
			.Do("Unlock", d => d.Unlock());

		var report = spec.Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().Be(3);
	}

	[Test]
	public void A_mutable_class_with_a_bug_is_found_with_the_actions_that_reach_it()
	{
		var spec = EntitySpec
			.ForMutable(() => new MutableDoor { LockIsBroken = true }, DoorConstraints.All.Map<MutableDoor>(d => new Door(d.State)))
			.Do("Open", d => d.Open())
			.Do("Lock", d => d.Lock());

		var act = () => spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("LegalTransitions");
	}

	[Test]
	public void Mutable_actions_can_take_a_finite_set_of_arguments()
	{
		var seen = new HashSet<int>();
		var spec = EntitySpec.ForMutable(() => new List<int>(), snapshot: l => string.Join(",", l)).Do("Add", [1, 2], (List<int> l, int n) =>
		{
			seen.Add(n);
			if (l.Count < 2)
			{
				l.Add(n);
			}
		});

		spec.Exhaustive(requireCoverage: false);

		seen.Should().BeEquivalentTo([1, 2]);
	}
}
