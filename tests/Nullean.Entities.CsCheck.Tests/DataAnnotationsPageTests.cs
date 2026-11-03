using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

[Constraints]
public sealed partial record AnnotatedSignUp(
	[Required] string Name,
	[Range(18, 120)] int Age,
	[RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference,
	[StringLength(10, MinimumLength = 3)] string Handle
);

// The examples on docs/integration/data-annotations.md.
public class DataAnnotationsPageTests
{
	private static List<AnnotatedSignUp> Draw(AutoMode mode, int count = 2000)
	{
		var gen = GenAuto.Of<AnnotatedSignUp>(mode);
		return [.. Enumerable.Range(0, count).Select(i => gen.Generate(new PCG(11, (ulong)i + 1), null, out _))];
	}

	[Test]
	public void An_annotated_record_is_a_constraint_set_named_after_its_members()
	{
		var violation = AnnotatedSignUp.Constraints.Validate(new AnnotatedSignUp("Ada", 12, "ABC-123", "ada")).Should().ContainSingle().Which;

		violation.ConstraintId.Should().Be("age-range");
		violation.Member.Should().Be("Age");
		violation.Message.Should().Be("The field Age must be between 18 and 120.");
	}

	[Test]
	public void AutoTest_reads_the_same_rules() => AutoTest.For<AnnotatedSignUp>().Should().NotBeNull();

	[Test]
	public void Generated_data_breaks_every_annotation() => AnnotatedSignUp.Constraints.Probe().NeverViolated.Should().BeEmpty();

	[Test]
	public void Plausible_data_satisfies_the_annotations() =>
		Draw(AutoMode.Plausible).Should().OnlyContain(s => s.Age >= 18 && s.Age <= 120 && s.Handle.Length >= 3 && s.Handle.Length <= 10);

	[Test]
	public void Mixed_data_reaches_both_sides_of_each_boundary()
	{
		var values = Draw(AutoMode.Mixed);

		values.Select(s => s.Age).Should().Contain([17, 18, 120, 121]);
		values.Select(s => s.Handle?.Length ?? -1).Should().Contain([2, 3, 10, 11]);
	}
}
