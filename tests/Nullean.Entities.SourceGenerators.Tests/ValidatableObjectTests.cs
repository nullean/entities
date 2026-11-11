using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace Nullean.Entities.SourceGenerators.Tests;

// [Constraints(ValidatableObject = true)]: the generated shape and its diagnostics. What the generated Validate reports at runtime is tested in
// tests/Nullean.Entities.Tests/ValidatableObjectTests.cs and, behind minimal API AddValidation(), in tests/Nullean.Entities.AspNetCore.Tests.
public class ValidatableObjectGeneratorTests
{
	private static string Source(string attribute, string extra = "") =>
		$$"""
		#nullable enable
		using System.ComponentModel.DataAnnotations;
		using Nullean.Entities;

		namespace Sample;

		[Constraints{{attribute}}]
		public partial class SignUp {{extra}}
		{
			[Required] public string? Name { get; set; }
			[Range(18, 120)] public int Age { get; set; }
			public required string Email { get; init; }
			public System.DateTime Start { get; set; }
			public System.DateTime End { get; set; }

			public static readonly Constraint<SignUp> EndAfterStart = Constraints<SignUp>.Always(s => s.End > s.Start, "End must be after Start");
		}
		""";

	private static void CompilesCleanly(GeneratorRun run) =>
		run.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

	[Test]
	public void Off_by_default_the_type_does_not_implement_IValidatableObject()
	{
		var run = GeneratorTestHost.Run(Source(""));

		run.Diagnostics.Should().BeEmpty();
		run.SingleGeneratedSource().Should().NotContain("IValidatableObject");
		CompilesCleanly(run);
	}

	[Test]
	public void On_it_implements_IValidatableObject_and_skips_only_the_rules_that_come_from_attributes()
	{
		var run = GeneratorTestHost.Run(Source("(ValidatableObject = true)"));

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain(": global::Nullean.Entities.IHaveConstraints<SignUp>, global::System.ComponentModel.DataAnnotations.IValidatableObject");
		source.Should().Contain("global::System.ComponentModel.DataAnnotations.IValidatableObject.Validate(");
		// The attribute rules are skipped; the implicit email-required rule and the hand-written rule are not.
		var skip = source.Split('\n').Single(l => l.Contains("violation.ConstraintId is"));
		skip.Should().Contain("\"name-required\"").And.Contain("\"age-range\"").And.NotContain("email-required").And.NotContain("end-after-start");
		CompilesCleanly(run);

		var signUp = run.OutputCompilation.GetTypeByMetadataName("Sample.SignUp")!;
		signUp.AllInterfaces.Select(i => i.ToDisplayString()).Should().Contain("System.ComponentModel.DataAnnotations.IValidatableObject");
	}

	[Test]
	public void A_type_with_no_attribute_rule_gets_no_skip_list()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			[Constraints(ValidatableObject = true)]
			public partial record struct Window(int From, int To)
			{
				public static readonly Constraint<Window> Ordered = Constraints<Window>.Always(w => w.From <= w.To, "From after To");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		run.SingleGeneratedSource().Should().Contain("IValidatableObject.Validate(").And.NotContain("ConstraintId is");
		CompilesCleanly(run);
	}

	[Test]
	public void A_type_with_no_rule_at_all_still_gets_the_interface()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			[Constraints(ValidatableObject = true)]
			public partial class Empty { public int N { get; set; } }
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		run.SingleGeneratedSource().Should().Contain("IValidatableObject.Validate(");
		CompilesCleanly(run);
	}

	[Test]
	public void A_type_that_already_implements_IValidatableObject_is_NECG011_and_gets_no_second_one()
	{
		var run = GeneratorTestHost.Run(
			Source("(ValidatableObject = true)", ": IValidatableObject").Replace(
				"public static readonly",
				"public System.Collections.Generic.IEnumerable<ValidationResult> Validate(ValidationContext c) => [];\n\tpublic static readonly",
				StringComparison.Ordinal
			)
		);

		var error = run.Diagnostics.Should().ContainSingle(d => d.Id == "NECG011").Which;
		error.Severity.Should().Be(DiagnosticSeverity.Error);
		error.GetMessage().Should().Contain("SignUp");
		// The set is still generated, without the interface.
		run.SingleGeneratedSource().Should().Contain("Constraints").And.NotContain("IValidatableObject");
		CompilesCleanly(run);
	}

	[Test]
	public void A_base_type_that_implements_IValidatableObject_is_NECG011_too()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			public abstract class Entity : IValidatableObject
			{
				public System.Collections.Generic.IEnumerable<ValidationResult> Validate(ValidationContext c) => [];
			}

			[Constraints(ValidatableObject = true)]
			public partial class Customer : Entity
			{
				[Required] public string? Name { get; set; }
			}
			"""
		);

		run.Diagnostics.Should().ContainSingle(d => d.Id == "NECG011");
		run.SingleGeneratedSource().Should().NotContain("IValidatableObject");
	}

	// Listing the interface without implementing it is how another generator (Microsoft.Extensions.Validation's) gets to see it: this one writes Validate.
	[Test]
	public void A_type_that_lists_IValidatableObject_without_implementing_it_gets_Validate_and_no_second_listing()
	{
		var run = GeneratorTestHost.Run(Source("(ValidatableObject = true)", ": IValidatableObject"));

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("IValidatableObject.Validate(");
		source.Should().NotContain("IHaveConstraints<SignUp>, global::System.ComponentModel.DataAnnotations.IValidatableObject");
		CompilesCleanly(run);
	}

	[Test]
	public void A_companion_whose_rules_are_about_another_type_is_NECG012()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public record Order(int Total);

			[Constraints(ValidatableObject = true)]
			public static partial class OrderRules
			{
				public static readonly Constraint<Order> Positive = Constraints<Order>.Always(o => o.Total > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().ContainSingle(d => d.Id == "NECG012").Which.Severity.Should().Be(DiagnosticSeverity.Error);
		run.SingleGeneratedSource().Should().NotContain("IValidatableObject");
	}
}
