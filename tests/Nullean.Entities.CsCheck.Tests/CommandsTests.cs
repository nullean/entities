using AwesomeAssertions;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal abstract record Cmd
{
	public sealed record Add(int Amount) : Cmd;

	public sealed record Reset : Cmd;
}

public class CommandsTests
{
	[Test]
	public void Over_makes_one_command_per_listed_value()
	{
		var commands = Commands.Over(a => new Cmd.Add(a), 1, 5);

		commands.Should().Equal(new Cmd.Add(1), new Cmd.Add(5));
	}

	[Test]
	public void FromDomain_uses_the_same_values_an_action_argument_gets_including_the_edges()
	{
		var amounts = Commands.FromDomain<int, Cmd.Add>(a => new Cmd.Add(a)).Select(c => c.Amount).ToList();

		amounts.Should().Contain(0).And.Contain(-1);
		Commands.FromDomain<int, Cmd.Add>(a => new Cmd.Add(a), AutoMode.Plausible).Select(c => c.Amount).Should().NotContain(-1);
	}

	[Test]
	public void Commands_of_several_kinds_drive_an_event_sourced_model_and_the_bad_argument_is_caught()
	{
		static IEnumerable<string> Decide(int total, Cmd c) =>
			c is Cmd.Add { Amount: var a } && total + a <= 3 ? [$"added {a}"] : c is Cmd.Reset ? ["reset"] : [];

		static int Evolve(int total, string e) =>
			e.StartsWith("added", StringComparison.Ordinal) ? total + int.Parse(e[6..], System.Globalization.CultureInfo.InvariantCulture) : 0;

		var nonNegative = ConstraintSet.From(("NotNegative", Constraints<int>.Always(n => n >= 0)));
		var commands = Commands.Of<Cmd>(Commands.Over(a => new Cmd.Add(a), 1, 2), [new Cmd.Reset()]);

		var correct = EntitySpec.EventSourced(0, Decide, Evolve, commands, nonNegative).Spec.Exhaustive();
		correct.Closed.Should().BeTrue();

		// the same model with -1 in the domain: the amount guard only checks the upper bound, so the total can go negative
		var withNegative = Commands.Of<Cmd>(Commands.Over(a => new Cmd.Add(a), 1, -1), [new Cmd.Reset()]);
		var act = () => EntitySpec.EventSourced(0, Decide, Evolve, withNegative, nonNegative).Spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("NotNegative");
	}
}
