using AwesomeAssertions;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record Toggle(bool On)
{
	public Toggle Flip() => new(!On);
}

public class NoOpsTests
{
	private static EntitySpec<Door> DoorSpec() => DoorConstraints.All.Spec(Door.New(), DoorConstraints.Actions);

	[Test]
	public void An_action_that_does_nothing_is_caught_by_the_Allow_edge_it_no_longer_takes()
	{
		var report = DoorSpec().NoOps();

		report.Results.Select(r => r.Action).Should().BeEquivalentTo("Open", "Close", "Lock", "Unlock");
		report.Results.Should().OnlyContain(r => r.Outcome == NoOpOutcome.CaughtByCoverage);
		report.Results.Single(r => r.Action == "Lock").By.Should().Contain("Closed->Locked");
	}

	[Test]
	public void Faults_alone_cannot_see_it_and_NoOps_can()
	{
		// the automatic "X has no effect" faults are reported but caught by nothing: only coverage notices them
		DoorSpec().Faults(throwOnUncaught: false).Uncaught.Should().NotBeEmpty();
		DoorSpec().NoOps().Undetected.Should().BeEmpty();
	}

	[Test]
	public void An_action_nothing_notices_is_reported_and_throws()
	{
		var spec = ConstraintSet.From(("anything", Constraints<Toggle>.Always(_ => true, "always"))).Spec(new Toggle(false)).Action("Flip", t => t.Flip());

		spec.NoOps(throwOnUndetected: false).Undetected.Should().Equal("Flip");
		var act = () => spec.NoOps();
		act.Should().Throw<Exception>().Which.Message.Should().Contain("Flip");
	}

	[Test]
	public void An_action_a_requirement_notices_is_caught_by_it()
	{
		var spec = ConstraintSet
			.From(("anything", Constraints<Toggle>.Always(_ => true, "always")))
			.Spec(new Toggle(false))
			.Action("Flip", t => t.Flip())
			.Reachable("gets switched on", t => t.On);

		var report = spec.NoOps();

		report.Results.Single().Outcome.Should().Be(NoOpOutcome.CaughtByRequirement);
	}
}
