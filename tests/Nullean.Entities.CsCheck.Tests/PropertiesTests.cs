using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

public class ConstraintPropertiesTests
{
	private static readonly ConstraintSet<Door> NotLocked = ConstraintSet.From(
		("NotLocked", Constraints<Door>.Always(d => d.State != DoorState.Locked, "must not be locked"))
	);

	[Test]
	public void ForAll_runs_the_property_over_values_that_satisfy_the_constraints() =>
		NotLocked.ForAll(d => d.State != DoorState.Locked, iter: 100);

	[Test]
	public void ForAll_reports_a_failing_property_with_a_replayable_seed()
	{
		var act = () => NotLocked.ForAll(d => d.State == DoorState.Closed, iter: 200);

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("Set seed");
	}

	[Test]
	public void ForAll_accepts_an_assertion_style_property() =>
		DoorConstraints.All.ForAll(
			d => d.Close().Open().State.Should().BeOneOf(DoorState.Open, DoorState.Closed, DoorState.Locked),
			GenAuto.Of<Door>(AutoMode.Plausible),
			iter: 50
		);

	[Test]
	public void Preserves_passes_when_every_action_keeps_the_door_valid() => DoorConstraints.All.Preserves(DoorConstraints.Actions, iter: 200);

	[Test]
	public void Preserves_finds_an_action_that_breaks_a_constraint_and_names_it()
	{
		var broken = new EntityActions<Door>(new EntityAction<Door>("ForceOpen", null, (d, _) => d with { State = DoorState.Open }));

		var act = () => DoorConstraints.All.Preserves(broken, iter: 200);

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("ForceOpen").And.Contain("LegalTransitions");
	}

	[Test]
	public void Preserves_without_actions_says_so()
	{
		var act = () => DoorConstraints.All.Preserves(new EntityActions<Door>());

		act.Should().Throw<InvalidOperationException>();
	}

	[Test]
	public void Probe_lists_a_constraint_that_never_fails()
	{
		var report = DoorConstraints.All.Probe(samples: 200);

		report.NeverViolated.Should().Contain("LockedDoorsAreNotOpen");
	}

	[Test]
	public void Probe_counts_violations_of_a_constraint_the_generator_can_break()
	{
		var report = NotLocked.Probe(samples: 300);

		report.Entries.Single().Violated.Should().BeGreaterThan(0);
		report.Entries.Single().Held.Should().BeGreaterThan(0);
	}

	[Test]
	public void Probe_throws_on_a_constraint_nothing_can_satisfy()
	{
		var contradiction = ConstraintSet.From(("Never", Constraints<Door>.Always(_ => false, "impossible")));

		var act = () => contradiction.Probe(samples: 50);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Never");
	}

	[Test]
	public void RejectsInvalid_checks_violating_values_are_named_and_rejected_by_Apply()
	{
		var report = NotLocked.RejectsInvalid(attempts: 300);

		report.Entries.Single().Checked.Should().BeGreaterThan(0);
	}

	[Test]
	public void RejectsInvalid_reports_a_constraint_the_generator_never_violates()
	{
		var report = DoorConstraints.All.RejectsInvalid(attempts: 200);

		report.Unfalsifiable.Should().Contain("LockedDoorsAreNotOpen");
	}

	[Test]
	public void Valid_explains_which_constraints_rejected_everything()
	{
		var contradiction = ConstraintSet.From(("Never", Constraints<Door>.Always(_ => false, "impossible")));
		var gen = GenAuto.Of<Door>().Valid(contradiction);

		var act = () => gen.Generate(new PCG(1u, 1UL), null, out _);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Never");
	}
}
