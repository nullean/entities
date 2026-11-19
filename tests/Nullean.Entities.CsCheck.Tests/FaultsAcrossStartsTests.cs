using AwesomeAssertions;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal enum SwitchMode
{
	Up,
	Down
}

// Two kinds of switch: Raise only works on an Up one, Lower only on a Down one, and so does the fault of each.
internal sealed record Switch(SwitchMode Mode, int Level)
{
	public Switch Raise() => Mode == SwitchMode.Up && Level < 2 ? this with { Level = Level + 1 } : this;

	public Switch Lower() => Mode == SwitchMode.Down && Level > 0 ? this with { Level = Level - 1 } : this;
}

public class FaultsAcrossStartsTests
{
	private static readonly Switch UpSwitch = new(SwitchMode.Up, 0);
	private static readonly Switch DownSwitch = new(SwitchMode.Down, 2);

	private static EntitySpec<Switch> Spec() =>
		ConstraintSet
			.From(("level-in-range", Constraints<Switch>.Always(s => s.Level is >= 0 and <= 2, "A level is 0 to 2.")))
			.Spec(UpSwitch)
			.Action("Raise", s => s.Raise())
			.Action("Lower", s => s.Lower())
			.Fault("Raise overshoots", "Raise", (_, after) => after.Mode == SwitchMode.Up ? after with { Level = after.Level + 5 } : after)
			.Fault("Lower undershoots", "Lower", (_, after) => after.Mode == SwitchMode.Down ? after with { Level = after.Level - 5 } : after);

	[Test]
	public void A_fault_only_one_start_can_trigger_is_caught_whichever_start_comes_first()
	{
		var upFirst = () => Spec().From(UpSwitch, DownSwitch).Faults();
		var downFirst = () => Spec().From(DownSwitch, UpSwitch).Faults();

		upFirst.Should().NotThrow();
		downFirst.Should().NotThrow();
	}

	[Test]
	public void A_fault_no_start_can_trigger_is_said_to_never_fire()
	{
		// both starts are Up: the Lower fault only changes a Down switch, so it changes no step from either start
		var act = () => Spec().From(UpSwitch, new Switch(SwitchMode.Up, 1)).Faults();

		var message = act.Should().Throw<Exception>().Which.Message;
		message.Split('\n')[0].Should().Contain("Lower undershoots").And.NotContain("Raise overshoots");
		message.Should().Contain("Never triggered: Lower undershoots");
	}

	[Test]
	public void A_fault_no_requirement_catches_still_fails_from_every_start()
	{
		var act =
			() => ConstraintSet
				.From(("level-in-range", Constraints<Switch>.Always(s => s.Level is >= 0 and <= 5, "A level is 0 to 5.")))
				.Spec(UpSwitch)
				.Action("Raise", s => s.Raise())
				.Fault("Raise stays put", "Raise", (before, _) => before)
				.From(UpSwitch, DownSwitch)
				.Faults();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("Raise stays put").And.Contain("from none of the 2 starts");
	}

	[Test]
	public void The_output_says_which_faults_were_caught_across_the_starts()
	{
		var lines = new List<string>();

		_ = Spec().From(UpSwitch, DownSwitch).Faults(lines.Add);

		lines.Should().Contain(
			l => l.StartsWith("Across 2 starts", StringComparison.Ordinal) && l.Contains(
				"Lower undershoots: caught by level-in-range",
				StringComparison.Ordinal
			)
		);
	}

	[Test]
	public void With_one_start_the_behaviour_is_unchanged()
	{
		var act = () => Spec().From(UpSwitch).Faults();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("Lower undershoots");
	}
}
