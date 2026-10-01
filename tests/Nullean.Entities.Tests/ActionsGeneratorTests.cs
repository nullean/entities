using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal enum Phase
{
	Draft,
	Live
}

internal sealed record Page(Phase Phase, string Title)
{
	public Page Publish() => this with { Phase = Phase.Live };

	public Page Rename(string title) => this with { Title = title };

	[ConstraintIgnore]
	public Page Internal() => this;

	// Not actions: wrong return type, two parameters, static, generic.
	public bool IsLive() => Phase == Phase.Live;

	public Page Both(string a, string b) => this with { Title = a + b };

	public static Page Create() => new(Phase.Draft, "");

	public Page Generic<T>(T value) => this;
}

[Constraints]
internal static partial class PageConstraints
{
	public static readonly Constraint<Page> LegalPhases = Constraints<Page>.Transitions(p => p.Phase).Allow(Phase.Draft, Phase.Live);
}

public class ActionsGeneratorTests
{
	[Test]
	public void Actions_lists_only_public_instance_methods_returning_the_entity_with_at_most_one_argument() =>
		PageConstraints.Actions.Select(a => a.Name).Should().BeEquivalentTo("Publish", "Rename");

	[Test]
	public void Actions_carry_the_argument_type_and_run_the_method()
	{
		var rename = PageConstraints.Actions.Single(a => a.Name == "Rename");
		var publish = PageConstraints.Actions.Single(a => a.Name == "Publish");

		rename.ArgumentType.Should().Be(typeof(string));
		publish.ArgumentType.Should().BeNull();
		rename.Apply(Page.Create(), "Home").Title.Should().Be("Home");
		publish.Apply(Page.Create(), null).Phase.Should().Be(Phase.Live);
	}

	[Test]
	public void Transitions_allow_list_ignores_a_mutation_that_leaves_the_selected_state_alone()
	{
		var result = PageConstraints.All.Apply(Page.Create(), p => p.Rename("Home"));

		result.IsOk.Should().BeTrue();
	}

	[Test]
	public void Transitions_allow_list_still_rejects_a_change_that_is_not_listed()
	{
		var live = Page.Create().Publish();

		var result = PageConstraints.All.Apply(live, p => p with { Phase = Phase.Draft });

		result.IsFail.Should().BeTrue();
	}
}
