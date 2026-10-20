using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// Testing a service by the state it is in: a correct order service and one with a classic bug (it ships an order that was never confirmed).

public abstract record ServiceOrderState
{
	public sealed record Draft : ServiceOrderState;

	public sealed record Placed(int Lines) : ServiceOrderState;

	public sealed record Confirmed : ServiceOrderState;

	public sealed record Shipped : ServiceOrderState;

	public sealed record Cancelled : ServiceOrderState;
}

internal sealed class ServiceOrders(TimeProvider clock, bool shipsUnconfirmed = false)
{
	private ServiceOrderState _state = new ServiceOrderState.Draft();

	public ServiceOrderState State => _state;

	public DateTimeOffset LastChange { get; private set; } = clock.GetUtcNow();

	// A real service would load and save through a repository; the awaits here complete on their own, which is what makes the run deterministic.
	public Task PlaceAsync() => Do(() => _state is ServiceOrderState.Draft ? new ServiceOrderState.Placed(0) : null);

	public Task AddLineAsync() => Do(() => _state is ServiceOrderState.Placed p ? p with { Lines = p.Lines + 1 } : null);

	public Task ConfirmAsync() => Do(() => _state is ServiceOrderState.Placed { Lines: > 0 } ? new ServiceOrderState.Confirmed() : null);

	public Task ShipAsync() =>
		Do(() => _state is ServiceOrderState.Confirmed || (shipsUnconfirmed && _state is ServiceOrderState.Placed) ? new ServiceOrderState.Shipped() : null);

	public Task CancelAsync() => Do(() => _state is ServiceOrderState.Placed or ServiceOrderState.Confirmed ? new ServiceOrderState.Cancelled() : null);

	private Task Do(Func<ServiceOrderState?> next)
	{
		if (next() is { } state)
		{
			_state = state;
			LastChange = clock.GetUtcNow();
		}
		return Task.CompletedTask;
	}
}

public class ServiceTestingTests
{
	private static readonly StateMachine<ServiceOrderState> Lifecycle = StateMachine<ServiceOrderState>.Define(
		m =>
			m
				.Initial<ServiceOrderState.Draft>(s => s.On("Place").To<ServiceOrderState.Placed>(_ => new ServiceOrderState.Placed(0)))
				.State<ServiceOrderState.Placed>(
					s =>
						s
							.On("AddLine").To<ServiceOrderState.Placed>(p => p with { Lines = p.Lines + 1 })
							.On("Confirm").To<ServiceOrderState.Confirmed>(_ => new ServiceOrderState.Confirmed(), when: p => p.Lines > 0)
							.On("Cancel").To<ServiceOrderState.Cancelled>(_ => new ServiceOrderState.Cancelled())
				)
				.State<ServiceOrderState.Confirmed>(
					s =>
						s
							.On("Ship").To<ServiceOrderState.Shipped>(_ => new ServiceOrderState.Shipped())
							.On("Cancel").To<ServiceOrderState.Cancelled>(_ => new ServiceOrderState.Cancelled())
				)
				.Terminal<ServiceOrderState.Shipped>()
				.Terminal<ServiceOrderState.Cancelled>()
	);

	private static Task Drive(ServiceOrders service, string trigger) => trigger switch
	{
		"Place" => service.PlaceAsync(),
		"AddLine" => service.AddLineAsync(),
		"Confirm" => service.ConfirmAsync(),
		"Ship" => service.ShipAsync(),
		"Cancel" => service.CancelAsync(),
		_ => throw new InvalidOperationException($"No operation for '{trigger}'"),
	};

	private static Script<ServiceOrders> ScriptFor() =>
		Script
			.For<ServiceOrders>()
			.StepAsync("Place", s => s.PlaceAsync())
			.StepAsync("AddLine", s => s.AddLineAsync())
			.StepAsync("Confirm", s => s.ConfirmAsync())
			.StepAsync("Ship", s => s.ShipAsync())
			.StepAsync("Cancel", s => s.CancelAsync())
			.Length(1, 12);

	[Test]
	public void A_correct_service_conforms_to_its_machine()
	{
		Lifecycle.ConformsToAsync(clock => new ServiceOrders(clock), s => s.State, Drive, iter: 300);
	}

	[Test]
	public async Task A_service_that_ships_an_unconfirmed_order_is_caught_with_a_short_sequence()
	{
		var act = () => Lifecycle.ConformsToAsync(clock => new ServiceOrders(clock, shipsUnconfirmed: true), s => s.State, Drive, iter: 500);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("the trigger is refused, so it stays in").And.Contain("Ship");
	}

	[Test]
	public async Task A_service_that_does_not_follow_the_machine_in_the_other_direction_is_caught_too()
	{
		// Confirm is wired to nothing: the machine allows it, the service never moves.
		var act = () =>
			Lifecycle.ConformsToAsync(
				clock => new ServiceOrders(clock),
				s => s.State,
				(s, trigger) => trigger == "Confirm" ? Task.CompletedTask : Drive(s, trigger),
				iter: 500
			);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("the machine says the service moves to");
	}

	[Test]
	public async Task SampleState_checks_the_machines_rule_on_every_step_of_a_correct_service()
	{
		var rules = ConstraintSet.From(("lifecycle", Lifecycle.AsConstraint()));

		await ScriptFor().SampleStateAsync(clock => new ServiceOrders(clock), s => s.State, rules, iter: 300);
	}

	[Test]
	public async Task SampleState_catches_a_forbidden_transition_in_a_buggy_service()
	{
		var rules = ConstraintSet.From(("lifecycle", Lifecycle.AsConstraint()));

		var act = async () =>
			await ScriptFor().SampleStateAsync(clock => new ServiceOrders(clock, shipsUnconfirmed: true), s => s.State, rules, seed: "0N0XIzNsQ0O2", iter: 500);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("lifecycle").And.Contain("Placed");
	}

	[Test]
	public async Task SampleState_also_checks_an_invariant_about_the_state()
	{
		var rules = ConstraintSet.From(
			("lines-stay-small", Constraints<ServiceOrderState>.Always(
				s => s is not ServiceOrderState.Placed { Lines: > 3 },
				"An order has at most three lines"
			))
		);

		// Reaching more than three lines takes a Place and then several AddLines in a row, which a random run does not always draw: enough iterations that it does, every time.
		var act = async () => await ScriptFor().Length(8, 12).SampleStateAsync(clock => new ServiceOrders(clock), s => s.State, rules, iter: 5_000);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("at most three lines");
	}

	[Test]
	public async Task SampleState_advances_the_clock_the_service_reads()
	{
		var script = ScriptFor().Delays(Gen.Int[1, 60].Select(s => TimeSpan.FromSeconds(s)));
		var rules = ConstraintSet.From(
			("clock-never-goes-back", Constraints<(DateTimeOffset At, ServiceOrderState State)>.Transitions((a, b) => b.At >= a.At, "Time went backwards"))
		);

		await script.SampleStateAsync(clock => new ServiceOrders(clock), s => (s.LastChange, s.State), rules, iter: 100);
	}
}
