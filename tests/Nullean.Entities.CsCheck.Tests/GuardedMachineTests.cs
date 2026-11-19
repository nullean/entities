using AwesomeAssertions;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal enum RetryState
{
	Pending,
	Failed
}

// Retries are allowed while attempts are left: the diagram says Retry: Failed -> Pending, the entity refuses it after Budget attempts.
internal sealed record RetryJob(RetryState State, int Attempts, int Budget = 3)
{
	public static RetryJob New(int budget = 3) => new(RetryState.Pending, 0, budget);

	public RetryJob Fail() => State == RetryState.Pending ? this with { State = RetryState.Failed, Attempts = Attempts + 1 } : this;

	public RetryJob Retry() => State == RetryState.Failed && Attempts < Budget ? this with { State = RetryState.Pending } : this;
}

internal sealed class MutableRetryJob(int budget = 3)
{
	public RetryState State { get; private set; } = RetryState.Pending;

	public int Attempts { get; private set; }

	public int Budget { get; } = budget;

	public void Fail()
	{
		if (State == RetryState.Pending)
		{
			State = RetryState.Failed;
			Attempts++;
		}
	}

	public void Retry()
	{
		if (State == RetryState.Failed && Attempts < Budget)
		{
			State = RetryState.Pending;
		}
	}
}

public class GuardedMachineTests
{
	private static readonly StateMachine<RetryState, string> Unguarded = StateMachine
		.Define<RetryState, string>(RetryState.Pending)
		.Permit("Fail", RetryState.Pending, RetryState.Failed)
		.Permit("Retry", RetryState.Failed, RetryState.Pending)
		.Build();

	private static readonly StateMachine<RetryState, string> Guarded = StateMachine
		.Define<RetryState, string>(RetryState.Pending)
		.Permit("Fail", RetryState.Pending, RetryState.Failed)
		.Permit<RetryJob>("Retry", RetryState.Failed, RetryState.Pending, when: j => j.Attempts < 3)
		.Build();

	private static readonly StateMachine<RetryState, string> GuardedMutable = StateMachine
		.Define<RetryState, string>(RetryState.Pending)
		.Permit("Fail", RetryState.Pending, RetryState.Failed)
		.Permit<MutableRetryJob>("Retry", RetryState.Failed, RetryState.Pending, when: j => j.Attempts < 3)
		.Build();

	private static (string, Func<RetryJob, RetryJob>)[] JobBindings => [("Fail", j => j.Fail()), ("Retry", j => j.Retry())];

	[Test]
	public void A_walk_long_enough_to_exhaust_the_budget_diverges_from_an_unguarded_edge()
	{
		var act = () => Unguarded.ConformTo(RetryJob.New(), j => j.State, iter: 2_000, bindings: JobBindings);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("the machine says Pending, but the entity is in Failed");
	}

	[Test]
	public void The_same_walks_conform_when_the_edge_carries_the_guard() =>
		Guarded.ConformTo(RetryJob.New(), j => j.State, iter: 2_000, bindings: JobBindings).Should().NotBeNull();

	[Test]
	public void An_entity_that_takes_the_edge_when_the_guard_is_false_is_caught()
	{
		// a budget of 5 retries past the machine's 3
		var act = () => Guarded.ConformTo(RetryJob.New(budget: 5), j => j.State, iter: 2_000, bindings: JobBindings);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("guard is false for the entity").And.Contain("Retry");
	}

	[Test]
	public void An_entity_that_refuses_the_edge_while_the_guard_is_true_is_caught()
	{
		// a budget of 1 refuses the second retry, which the machine still permits
		var act = () => Guarded.ConformTo(RetryJob.New(budget: 1), j => j.State, iter: 2_000, bindings: JobBindings);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("the machine says Pending, but the entity is in Failed");
	}

	[Test]
	public void A_class_that_changes_in_place_conforms_to_a_guarded_edge()
	{
		GuardedMutable
			.ConformToMutable(() => new MutableRetryJob(), j => j.State, iter: 2_000, bindings: [("Fail", j => j.Fail()), ("Retry", j => j.Retry())])
			.Should()
			.NotBeNull();

		var act =
			() => GuardedMutable.ConformToMutable(
				() => new MutableRetryJob(budget: 5),
				j => j.State,
				iter: 2_000,
				bindings: [("Fail", j => j.Fail()), ("Retry", j => j.Retry())]
			);
		act.Should().Throw<Exception>().Which.Message.Should().Contain("guard is false for the entity");
	}

	[Test]
	public void The_exhaustive_check_evaluates_the_guard_on_each_entity_it_reaches() =>
		Guarded.ConformToExhaustive(RetryJob.New(), j => j.State, bindings: JobBindings).ConformedTransitions.Should().BeGreaterThan(0);

	[Test]
	public void A_guarded_edge_stays_in_the_diagram_and_is_permitted_without_an_entity()
	{
		Guarded.ToMermaid().Should().Contain("Failed --> Pending : Retry");
		Guarded.Fire(RetryState.Failed, "Retry").Should().Be(RetryState.Pending);
		Guarded.Transitions.Single(t => t.Trigger == "Retry").Guard.Should().NotBeNull();
		Guarded.Spec().Exhaustive().States.Should().Be(2);
	}

	[Test]
	public void A_guard_written_for_another_entity_type_is_refused_up_front()
	{
		var act = () => Guarded.ConformTo(new MutableRetryJob(), j => j.State, bindings: [("Fail", j => j), ("Retry", j => j)]);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("RetryJob").And.Contain("MutableRetryJob");
	}
}
