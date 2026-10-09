using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

public class EventAutoTestTests
{
	private static readonly TemporalRuleSet<DeskEvent> Rules = TemporalRuleSet<DeskEvent>.From(
		("confirmed-ships-in-a-minute", Temporal<DeskEvent>
			.After<DeskEvent.Confirmed>()
			.Then<DeskEvent.Shipped>((c, s) => c.Id == s.Id)
			.Within(TimeSpan.FromSeconds(60))),
		("ids-are-positive", Temporal<DeskEvent>.Always<DeskEvent.Confirmed>(c => c.Id > 0)),
		("confirmed-is-shipped", Temporal<DeskEvent>.Eventually<DeskEvent.Shipped>(
			e => e is DeskEvent.Confirmed c ? c.Id : ((DeskEvent.Shipped)e).Id,
			e => e is DeskEvent.Confirmed
		))
	);

	private static Script<OrderDesk> Steps() => Script.For<OrderDesk>().Step("Confirm", Gen.Int[1, 3], (desk, id) => desk.Confirm(id)).Length(1, 4);

	private static EventAutoTest<OrderDesk, DeskEvent> Auto(bool ships, Script<OrderDesk>? script = null) =>
		EventAutoTest.For(Rules, script ?? Steps(), time => new OrderDesk(time, ships), desk => desk.Log);

	[Test]
	public void The_rules_say_which_axis_they_are_on_and_how_long_they_wait()
	{
		Rules.Rules.Select(r => r.Rule.Axis).Should().Equal(Axis.Temporal, Axis.Absence, Axis.Liveness);
		Rules.Rules[0].Rule.Shape.Window.Should().Be(TimeSpan.FromSeconds(60));
		Rules.Rules[2].Rule.Shape.Target.Should().NotBeNull();
	}

	[Test]
	public void A_service_that_answers_promptly_passes_every_event_axis()
	{
		var report = Auto(ships: true).Run();

		report.Results.Select(r => r.Axis).Should().Equal(Axis.Absence, Axis.Temporal, Axis.Liveness, Axis.Vacancy);
		report.Results.Should().AllBeOfType<AxisResult.Passed>();
	}

	[Test]
	public void A_service_that_never_answers_fails_temporal_with_delays_derived_from_the_window()
	{
		// the script sets no delays: the test tries 0 and just either side of 60s by itself
		var act = () => Auto(ships: false).Temporal();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("confirmed-ships-in-a-minute");
	}

	[Test]
	public void A_service_that_never_finishes_what_it_started_fails_liveness()
	{
		var act = () => Auto(ships: false).Liveness();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("confirmed-is-shipped");
	}

	[Test]
	public void An_absence_rule_that_the_service_breaks_fails_absence()
	{
		var rules = TemporalRuleSet<DeskEvent>.From(("ids-above-five", Temporal<DeskEvent>.Always<DeskEvent.Confirmed>(c => c.Id > 5)));

		var act = () => EventAutoTest.For(rules, Steps(), time => new OrderDesk(time, true), desk => desk.Log).Absence();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("ids-above-five");
	}

	[Test]
	public void A_run_that_never_opens_a_rule_is_vacuous_not_green()
	{
		var script = Script.For<OrderDesk>().Step("Confirm", Gen.Int[1, 3], (desk, id) => desk.Confirm(id)).Length(0, 0);

		var act = () => Auto(ships: true, script).Absence();

		act
			.Should()
			.Throw<VacuousPropertyException>()
			.Which
			.Message
			.Should()
			.Contain("ids-are-positive")
			.And
			.Contain("no run contained an event that opens it");
	}

	[Test]
	public void A_deadline_no_run_went_past_is_vacuous_even_though_nothing_failed()
	{
		var script = Steps().Delays(Gen.Const(TimeSpan.Zero));

		var act = () => Auto(ships: false, script).Temporal();

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("no run went past the 00:01:00 deadline");
	}

	[Test]
	public void An_axis_with_no_event_rule_is_not_applicable_and_not_a_case()
	{
		var rules = TemporalRuleSet<DeskEvent>.From(("ids-are-positive", Temporal<DeskEvent>.Always<DeskEvent.Confirmed>(c => c.Id > 0)));
		var auto = EventAutoTest.For(rules, Steps(), time => new OrderDesk(time, true), desk => desk.Log);

		auto.Temporal().Should().BeOfType<AxisResult.NotApplicable>();
		auto.Cases.Select(c => c.Axis).Should().Equal(Axis.Absence, Axis.Vacancy);
	}

	[Test]
	public void A_failing_case_carries_the_axis_law_and_consequence()
	{
		var act = () => Auto(ships: false).Cases.Single(c => c.Axis == Axis.Temporal).Run();

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Law: ").And.Contain("Consequence: ");
	}

	[Test]
	public void Events_exposed_as_an_async_stream_are_checked_the_same_way()
	{
		var auto = EventAutoTest.ForAsync(Rules, Steps(), time => new OrderDesk(time, false), desk => Stream(desk.Log));

		var act = () => auto.Temporal();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("confirmed-ships-in-a-minute");
	}

	private static async IAsyncEnumerable<Timestamped<DeskEvent>> Stream(IReadOnlyList<Timestamped<DeskEvent>> log)
	{
		foreach (var e in log)
		{
			await Task.Yield();
			yield return e;
		}
	}
}
