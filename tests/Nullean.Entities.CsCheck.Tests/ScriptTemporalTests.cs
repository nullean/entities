using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using TUnit;

namespace Nullean.Entities.CsCheck.Tests;

internal abstract record DeskEvent
{
	public sealed record Confirmed(int Id) : DeskEvent;

	public sealed record Shipped(int Id) : DeskEvent;
}

// An Rx-free service: it takes a TimeProvider and records what it did, with the time it did it.
internal sealed class OrderDesk(TimeProvider time, bool shipsPromptly)
{
	private readonly List<Timestamped<DeskEvent>> _log = [];

	public IReadOnlyList<Timestamped<DeskEvent>> Log => _log;

	public void Confirm(int id)
	{
		_log.Add(new Timestamped<DeskEvent>(time.GetUtcNow(), new DeskEvent.Confirmed(id)));
		if (shipsPromptly)
		{
			_log.Add(new Timestamped<DeskEvent>(time.GetUtcNow(), new DeskEvent.Shipped(id)));
		}
	}
}

public class ScriptTemporalTests
{
	private static readonly TemporalRuleSet<DeskEvent> Rules = TemporalRuleSet<DeskEvent>.From(
		("ConfirmedShipsInAMinute", Temporal<DeskEvent>
			.After<DeskEvent.Confirmed>()
			.Then<DeskEvent.Shipped>((c, s) => c.Id == s.Id)
			.Within(TimeSpan.FromSeconds(60)))
	);

	private static Script<OrderDesk> ConfirmSteps() =>
		Script
			.For<OrderDesk>()
			.Step("Confirm", Gen.Int[1, 3], (desk, id) => desk.Confirm(id))
			.Delays(Gen.Int[0, 120].Select(s => TimeSpan.FromSeconds(s)))
			.Length(2, 6);

	[Test]
	public void A_service_that_never_ships_is_caught_with_a_shrunk_walk_on_virtual_time()
	{
		var act = () => ConfirmSteps().SampleWith(time => new OrderDesk(time, shipsPromptly: false), desk => desk.Log, Rules, iter: 200);

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("ConfirmedShipsInAMinute").And.Contain("Set seed");
	}

	[Test]
	public void A_service_that_ships_straight_away_passes_whatever_the_delays()
	{
		var act = () => ConfirmSteps().SampleWith(time => new OrderDesk(time, shipsPromptly: true), desk => desk.Log, Rules, iter: 200);

		act.Should().NotThrow();
	}

	[Test]
	public void The_generated_delays_advance_the_providers_clock_not_the_wall_clock()
	{
		DateTimeOffset? seenAt = null;
		var script = Script
			.For<OrderDesk>()
			.Step("Confirm", Gen.Int[1, 1], (desk, id) => desk.Confirm(id))
			.Delays(Gen.Const(TimeSpan.FromHours(10)))
			.Length(1, 1);

		script.SampleWith(
			time => new OrderDesk(time, shipsPromptly: true),
			desk =>
			{
				seenAt = desk.Log[0].At;
				return desk.Log;
			},
			Rules,
			iter: 1
		);

		seenAt.Should().Be(new ManualTimeProvider().GetUtcNow().AddHours(10));
	}

	[Test]
	public void Coverage_fails_a_run_that_never_recorded_the_event_the_check_is_about()
	{
		// the rule is satisfied in every run, but nobody ever confirmed order 99, so a check about it proved nothing
		var coverage = new Coverage<DeskEvent>().Expect("Order 99 confirmed", e => e is DeskEvent.Confirmed { Id: 99 });

		var act = () => ConfirmSteps().SampleWith(time => new OrderDesk(time, shipsPromptly: true), desk => desk.Log, Rules, coverage, iter: 50);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("\"Order 99 confirmed\" never occurred");
	}

	[Test]
	public void Coverage_passes_when_the_event_is_recorded_in_enough_runs()
	{
		var coverage = new Coverage<DeskEvent>().Expect("Shipped", e => e is DeskEvent.Shipped, minPercentOfRuns: 50);

		var act = () => ConfirmSteps().SampleWith(time => new OrderDesk(time, shipsPromptly: true), desk => desk.Log, Rules, coverage, iter: 100);

		act.Should().NotThrow();
	}
}
