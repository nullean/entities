using AwesomeAssertions;

namespace Nullean.Entities.Tests;

public class StateMachineTests
{
	// ── A valid machine, mirroring the root README's OrderLifecycle example ──────

	private abstract record OrderState
	{
		public sealed record Draft : OrderState;

		public sealed record Placed(int Lines) : OrderState;

		public sealed record Confirmed : OrderState;

		public sealed record Shipped : OrderState;

		public sealed record Cancelled : OrderState;
	}

	private static StateMachine<OrderState> BuildValidMachine() =>
		StateMachine<OrderState>.Define(
			m => m
				.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>())
				.State<OrderState.Placed>(s => s.On("Confirm").To<OrderState.Confirmed>(when: p => p.Lines > 0).On("Cancel").To<OrderState.Cancelled>())
				.State<OrderState.Confirmed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
				.Terminal<OrderState.Shipped>()
				.Terminal<OrderState.Cancelled>()
		);

	[Test]
	public void Define_builds_the_declared_graph()
	{
		var machine = BuildValidMachine();

		machine.Initial.Should().Be<OrderState.Draft>();
		machine.States.Should().HaveCount(5);
		machine.Edges.Should().HaveCount(5);
		machine.IsTerminal(typeof(OrderState.Shipped)).Should().BeTrue();
		machine.IsTerminal(typeof(OrderState.Draft)).Should().BeFalse();
	}

	[Test]
	public void Unnamed_To_takes_its_target_type_name()
	{
		var machine = BuildValidMachine();
		machine.Edges.Should().ContainSingle(e => e.From == typeof(OrderState.Confirmed) && e.To == typeof(OrderState.Shipped) && e.Name == "Shipped");
	}

	[Test]
	public void Allows_matches_a_declared_transition_by_runtime_type()
	{
		var machine = BuildValidMachine();
		machine.Allows(new OrderState.Draft(), new OrderState.Placed(1)).Should().BeTrue();
	}

	[Test]
	public void Allows_rejects_an_undeclared_transition()
	{
		var machine = BuildValidMachine();
		machine.Allows(new OrderState.Draft(), new OrderState.Shipped()).Should().BeFalse();
	}

	[Test]
	public void Allows_honours_a_guard_on_the_source_value()
	{
		var machine = BuildValidMachine();
		machine.Allows(new OrderState.Placed(0), new OrderState.Confirmed()).Should().BeFalse();
		machine.Allows(new OrderState.Placed(1), new OrderState.Confirmed()).Should().BeTrue();
	}

	[Test]
	public void AsConstraint_allows_a_declared_transition()
	{
		var constraint = BuildValidMachine().AsConstraint();
		constraint.Check(new OrderState.Draft(), new OrderState.Placed(1)).Should().BeEmpty();
	}

	[Test]
	public void AsConstraint_rejects_an_undeclared_transition()
	{
		var constraint = BuildValidMachine().AsConstraint();
		constraint.Check(new OrderState.Draft(), new OrderState.Shipped()).Should().NotBeEmpty();
	}

	[Test]
	public void AsConstraint_rejects_a_guarded_transition_whose_guard_fails()
	{
		var constraint = BuildValidMachine().AsConstraint();
		constraint.Check(new OrderState.Placed(0), new OrderState.Confirmed()).Should().NotBeEmpty();
	}

	[Test]
	public void ToMermaid_renders_the_initial_edge_every_transition_and_every_terminal()
	{
		var mermaid = BuildValidMachine().ToMermaid();

		mermaid.Should().Contain("stateDiagram-v2");
		mermaid.Should().Contain("[*] --> Draft");
		mermaid.Should().Contain("Draft --> Placed : Place");
		mermaid.Should().Contain("Placed --> Confirmed : Confirm");
		mermaid.Should().Contain("Confirmed --> Shipped : Shipped");
		mermaid.Should().Contain("Placed --> Cancelled : Cancel");
		mermaid.Should().Contain("Shipped --> [*]");
		mermaid.Should().Contain("Cancelled --> [*]");
	}

	[Test]
	public void NewCoverageTracker_starts_fully_uncovered()
	{
		var machine = BuildValidMachine();
		var tracker = machine.NewCoverageTracker();

		tracker.FullyCovered.Should().BeFalse();
		tracker.Uncovered.Should().HaveCount(machine.Edges.Count);
	}

	[Test]
	public void NewCoverageTracker_drops_an_edge_once_its_pair_is_recorded()
	{
		var machine = BuildValidMachine();
		var tracker = machine.NewCoverageTracker();

		tracker.Record(new OrderState.Draft(), new OrderState.Placed(1));

		tracker.Uncovered.Should().HaveCount(machine.Edges.Count - 1);
		tracker.Uncovered.Should().NotContain(e => e.From == typeof(OrderState.Draft) && e.To == typeof(OrderState.Placed));
	}

	[Test]
	public void NewCoverageTracker_is_FullyCovered_once_every_edge_is_recorded()
	{
		var machine = BuildValidMachine();
		var tracker = machine.NewCoverageTracker();

		tracker.Record(new OrderState.Draft(), new OrderState.Placed(1));
		tracker.Record(new OrderState.Placed(1), new OrderState.Confirmed());
		tracker.Record(new OrderState.Placed(1), new OrderState.Cancelled());
		tracker.Record(new OrderState.Confirmed(), new OrderState.Shipped());
		tracker.Record(new OrderState.Confirmed(), new OrderState.Cancelled());

		tracker.FullyCovered.Should().BeTrue();
	}

	// ── NESM001: exhaustiveness ────────────────────────────────────────────────

	private abstract record IncompleteState
	{
		public sealed record A : IncompleteState;

		public sealed record B : IncompleteState;

		public sealed record C : IncompleteState;
	}

	[Test]
	public void Define_throws_NESM001_when_a_case_of_TState_is_never_declared()
	{
		var act = () => StateMachine<IncompleteState>.Define(m => m.Initial<IncompleteState.A>(s => s.To<IncompleteState.B>()).Terminal<IncompleteState.B>());
		// IncompleteState.C is never mentioned.

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM001");
	}

	[Test]
	public void Define_throws_NESM001_when_an_edge_points_at_a_type_nobody_declared()
	{
		// To<TTo> takes no constraint tying TTo to TState at all, so this compiles — DeadEndState.B
		// is declared via Terminal<B>() below, but the edge from A points at an unrelated type
		// nobody ever called Initial/State/Terminal on.
		var act = () => StateMachine<DeadEndState>.Define(m => m.Initial<DeadEndState.A>(s => s.To<string>()).Terminal<DeadEndState.B>());

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM001");
	}

	// ── NESM002: reachability ─────────────────────────────────────────────────

	private abstract record UnreachableState
	{
		public sealed record A : UnreachableState;

		public sealed record B : UnreachableState;

		public sealed record Orphan : UnreachableState;
	}

	[Test]
	public void Define_throws_NESM002_when_a_declared_state_has_no_path_from_Initial()
	{
		var act =
			() => StateMachine<UnreachableState>.Define(
				m => m.Initial<UnreachableState.A>(s => s.To<UnreachableState.B>()).Terminal<UnreachableState.B>().Terminal<UnreachableState.Orphan>()
			);
		// Orphan is declared (satisfies NESM001) but no edge leads to it.

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM002");
	}

	[Test]
	public void Define_throws_NESM002_when_no_Initial_state_is_declared()
	{
		var act =
			() => StateMachine<UnreachableState>.Define(
				m => m.Terminal<UnreachableState.A>().Terminal<UnreachableState.B>().Terminal<UnreachableState.Orphan>()
			);

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM002");
	}

	// ── NESM003: dead end ─────────────────────────────────────────────────────

	private abstract record DeadEndState
	{
		public sealed record A : DeadEndState;

		public sealed record B : DeadEndState;
	}

	[Test]
	public void Define_throws_NESM003_when_a_non_terminal_state_has_no_outgoing_transition()
	{
		var act = () => StateMachine<DeadEndState>.Define(m => m.Initial<DeadEndState.A>(s => s.To<DeadEndState.B>()).State<DeadEndState.B>());
		// B is declared Normal (not Terminal) but adds no outgoing transition of its own.

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM003");
	}

	[Test]
	public void Define_does_not_throw_NESM003_for_a_Terminal_state_with_no_outgoing_transition()
	{
		var act = () => StateMachine<DeadEndState>.Define(m => m.Initial<DeadEndState.A>(s => s.To<DeadEndState.B>()).Terminal<DeadEndState.B>());

		act.Should().NotThrow();
	}

	// ── NESM004: trap ─────────────────────────────────────────────────────────

	private abstract record TrapState
	{
		public sealed record A : TrapState;

		public sealed record B : TrapState;

		public sealed record C : TrapState;
	}

	[Test]
	public void Define_throws_NESM004_when_no_declared_state_can_reach_a_terminal()
	{
		var act =
			() => StateMachine<TrapState>.Define(
				m => m
					.Initial<TrapState.A>(s => s.On("Loop").To<TrapState.B>().On("Start").To<TrapState.C>())
					.State<TrapState.B>(s => s.On("Back").To<TrapState.A>())
					.State<TrapState.C>(s => s.On("Stay").To<TrapState.C>())
			);
		// A, B and C are all reachable from A, but none of them can ever reach a terminal —
		// B only loops back to A, C only loops to itself, and no Terminal<T>() is declared.

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM004");
	}

	[Test]
	public void Define_does_not_throw_NESM004_when_a_loop_still_has_an_exit_to_a_terminal()
	{
		var act =
			() => StateMachine<TrapState>.Define(
				m => m
					.Initial<TrapState.A>(s => s.On("Loop").To<TrapState.B>())
					.State<TrapState.B>(s => s.On("Back").To<TrapState.A>().On("Finish").To<TrapState.C>())
					.Terminal<TrapState.C>()
			);
		// A <-> B loops, but B can also exit to the terminal C — every state can still reach it.

		act.Should().NotThrow();
	}

	// ── Misuse ────────────────────────────────────────────────────────────────

	[Test]
	public void Define_throws_when_the_same_case_is_declared_twice()
	{
		var act = () => StateMachine<DeadEndState>.Define(m => m.Initial<DeadEndState.A>(s => s.To<DeadEndState.B>()).State<DeadEndState.A>());

		act.Should().Throw<InvalidOperationException>();
	}
}
