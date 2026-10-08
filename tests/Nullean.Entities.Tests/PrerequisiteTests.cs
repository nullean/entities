using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// A rule the others rely on ("the member is there") keeps them from running on a value that lacks it.
public class PrerequisiteTests
{
	public sealed record Page(string? Title, IReadOnlyList<string>? Tags, int Level);

	private static readonly Constraint<Page> TitleRequired = Constraints<Page>
		.Always(p => p.Title is not null, "The Title field is required.")
		.Named("title-required")
		.AsPrerequisite();

	// reads Title without a check: it was written for a value that has one
	private static readonly Constraint<Page> TitleShort = Constraints<Page>
		.Always(p => p.Title!.Length < 20, "The Title must be shorter than 20 characters.")
		.Named("title-short");

	private static readonly Constraint<Page> LevelPositive = Constraints<Page>
		.Always(p => p.Level > 0, "The Level must be positive.")
		.Named("level-positive");

	private static readonly ConstraintSet<Page> Rules = ConstraintSet.From(
		("title-required", TitleRequired),
		("title-short", TitleShort),
		("level-positive", LevelPositive)
	);

	[Test]
	public void AMissingPrerequisite_IsReported_AndTheRulesThatReadItAreNotAsked()
	{
		var violations = Rules.Validate(new Page(null, null, 0));

		violations.Select(v => v.ConstraintId).Should().Equal("title-required");
	}

	[Test]
	public void WhenNothingIsMissing_EveryRuleIsAsked()
	{
		var violations = Rules.Validate(new Page("a title that is far too long to be short", null, 0));

		violations.Select(v => v.ConstraintId).Should().BeEquivalentTo(["title-short", "level-positive"]);
	}

	[Test]
	public void ReportCarriesOnlyThePrerequisite_WhenOneIsMissing()
	{
		Rules.Report(new Page(null, null, 0)).Violations.Select(v => v.ConstraintId).Should().Equal("title-required");
	}

	[Test]
	public void ACheckedChange_IsGatedOnBothTheBeforeAndTheAfter()
	{
		var good = new Page("ok", null, 1);
		var missing = new Page(null, null, 1);

		Rules.Check(good, missing).Select(v => v.ConstraintId).Should().Equal("title-required");
		Rules.Check(missing, good).Select(v => v.ConstraintId).Should().Equal("title-required");
		Rules.Check(good, good with { Level = 0 }).Select(v => v.ConstraintId).Should().Equal("level-positive");
	}

	[Test]
	public void TheFlagSurvivesMapAndASetWithoutOne_IsNotGated()
	{
		TitleRequired.Map<(Page Page, int Other)>(x => x.Page).IsPrerequisite.Should().BeTrue();
		TitleShort.IsPrerequisite.Should().BeFalse();

		var ungated = ConstraintSet.From(("title-short", TitleShort), ("level-positive", LevelPositive));
		var act = () => ungated.Validate(new Page(null, null, 0));
		act.Should().Throw<NullReferenceException>("without a prerequisite nothing stops a rule from reading the missing member");
	}

	[Test]
	public void TheGeneratedRequiredRules_ArePrerequisites()
	{
		MustBeSetCompanion.All.AsConstraints().Single(c => c.Name == "title-required").IsPrerequisite.Should().BeTrue();
	}
}
