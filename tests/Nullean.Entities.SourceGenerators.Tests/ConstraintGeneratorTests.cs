using AwesomeAssertions;

namespace Nullean.Entities.SourceGenerators.Tests;

// Real end-to-end runtime behaviour is covered in tests/Nullean.Entities.Tests and
// tests/Nullean.Entities.Reactive.Tests, where the actual generator runs through the compiler's
// own analyzer pipeline on real test fixtures. These tests drive the generator in isolation
// instead, to pin down exact diagnostics and generated shape without a full build round-trip —
// and to reach failure modes (mixed kinds, generic types, ...) that a passing end-to-end fixture,
// by definition, never exercises.
public class ConstraintGeneratorShapeTests
{
	[Test]
	public void Self_attach_case_adds_IHaveConstraints_and_a_ConstraintSet_property()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Item(int Value)
			{
			    [Constraint]
			    public static readonly Constraint<Item> Positive = Constraints<Item>.Always(i => i.Value > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("global::Nullean.Entities.IHaveConstraints<Item>");
		source.Should().Contain("global::Nullean.Entities.ConstraintSet<global::Sample.Item>");
		source.Should().Contain("global::Nullean.Entities.ConstraintSet.From(");
		source.Should().Contain("(nameof(Positive), Positive)");
	}

	[Test]
	public void Companion_static_class_case_gets_a_nested_TypeConstraints_with_an_Instance_singleton()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total);

			public static partial class OrderSpec
			{
			    [Constraint]
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().NotContain("IHaveConstraints");
		// IConstraintsFor<T> is a plain instance interface, and OrderSpec's own `All` is
		// static either way — the interface never lands on the spec class's own base list.
		source.Should().NotContain("partial class OrderSpec :");
		source.Should().Contain("struct TypeConstraints : global::Nullean.Entities.IConstraintsFor<global::Sample.Order>");
		source.Should().Contain("public static readonly TypeConstraints Instance = default;");
		source.Should().Contain("global::Nullean.Entities.ConstraintSet<global::Sample.Order>");
		// The forwarding shortcut on the spec class itself — OrderSpec.Instance, not
		// OrderSpec.TypeConstraints.Instance — so a caller never has to spell out the nested type.
		source.Should().Contain("public static readonly TypeConstraints Instance = TypeConstraints.Instance;");
	}

	[Test]
	public void Companion_non_static_class_case_also_nests_TypeConstraints_rather_than_implementing_it_directly()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total);

			public sealed partial class OrderSpec
			{
			    [Constraint]
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		// Unlike the old static-abstract design, a non-static spec class still gets the interface
		// nested rather than directly implemented — IConstraintsFor<T>.All is an instance
		// member, and OrderSpec.All is (and stays) static regardless of whether OrderSpec
		// itself is `static`.
		source.Should().NotContain("partial class OrderSpec :");
		source.Should().Contain("struct TypeConstraints : global::Nullean.Entities.IConstraintsFor<global::Sample.Order>");
	}

	[Test]
	public void Reactive_case_bundles_into_a_ReactiveConstraintSet_with_named_tuples_and_a_nested_TypeConstraints()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;
			using Nullean.Entities.Reactive;

			namespace Sample;

			public abstract record Ev;

			public static partial class EvSpec
			{
			    [Constraint]
			    public static readonly ReactiveConstraint<Ev> AlwaysHolds = Constraints<Ev>.Always<Ev>(_ => true);
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("struct TypeConstraints : global::Nullean.Entities.Reactive.IReactiveConstraintsFor<global::Sample.Ev>");
		source.Should().Contain("public static readonly TypeConstraints Instance = default;");
		source.Should().Contain("public static readonly TypeConstraints Instance = TypeConstraints.Instance;");
		source.Should().Contain("global::Nullean.Entities.Reactive.ReactiveConstraintSet<global::Sample.Ev>");
		source.Should().Contain("global::Nullean.Entities.Reactive.ReactiveConstraintSet.From(");
		source.Should().Contain("(nameof(AlwaysHolds), AlwaysHolds)");
	}

	[Test]
	public void Non_partial_type_is_refused_with_NECG001()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public record Item(int Value)
			{
			    [Constraint]
			    public static readonly Constraint<Item> Positive = Constraints<Item>.Check(i => []);
			}
			"""
		);

		run.HasDiagnostic("NECG001").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Non_static_member_is_refused_with_NECG002()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Item(int Value)
			{
			    [Constraint]
			    public readonly Constraint<Item> Positive = Constraints<Item>.Check(i => []);
			}
			"""
		);

		run.HasDiagnostic("NECG002").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Wrong_member_type_is_refused_with_NECG003()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Item(int Value)
			{
			    [Constraint]
			    public static readonly int NotAConstraint = 1;
			}
			"""
		);

		run.HasDiagnostic("NECG003").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Generic_containing_type_is_refused_with_NECG004()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Box<T>(T Value)
			{
			    [Constraint]
			    public static readonly Constraint<Box<T>> NotNull = Constraints<Box<T>>.Check(b => []);
			}
			"""
		);

		run.HasDiagnostic("NECG004").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Nested_containing_type_is_refused_with_NECG005()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public class Outer
			{
			    public partial record Item(int Value)
			    {
			        [Constraint]
			        public static readonly Constraint<Item> Positive = Constraints<Item>.Check(i => []);
			    }
			}
			"""
		);

		run.HasDiagnostic("NECG005").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Mixed_sync_and_reactive_members_are_refused_with_NECG006()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;
			using Nullean.Entities.Reactive;

			namespace Sample;

			public abstract record Ev;

			public sealed record Item(int Value);

			public static partial class MixedSpec
			{
			    [Constraint]
			    public static readonly Constraint<Item> SyncOne = Constraints<Item>.Check(i => []);

			    [Constraint]
			    public static readonly ReactiveConstraint<Ev> ReactiveOne = Constraints<Ev>.Always<Ev>(_ => true);
			}
			"""
		);

		run.HasDiagnostic("NECG006").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Inconsistent_type_argument_is_refused_with_NECG007()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Item(int Value);

			public sealed record Order(decimal Total);

			public static partial class MixedSpec
			{
			    [Constraint]
			    public static readonly Constraint<Item> ItemRule = Constraints<Item>.Check(i => []);

			    [Constraint]
			    public static readonly Constraint<Order> OrderRule = Constraints<Order>.Check(o => []);
			}
			"""
		);

		run.HasDiagnostic("NECG007").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void One_invalid_member_blocks_generation_for_the_whole_type()
	{
		// Exhaustiveness is the point: a type with one good [Constraint] member and one bad one
		// must refuse entirely, not generate a Constraints set from the good one and silently
		// drop the bad one — that would itself be exactly the kind of drift [Constraint] exists
		// to prevent (see Diagnostics.cs).
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Item(int Value)
			{
			    [Constraint]
			    public static readonly Constraint<Item> Good = Constraints<Item>.Check(i => []);

			    [Constraint]
			    public readonly Constraint<Item> Bad = Constraints<Item>.Check(i => []);
			}
			"""
		);

		run.HasDiagnostic("NECG002").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Constraints_attribute_on_the_type_discovers_every_eligible_static_member_with_no_per_member_tagging()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total, int Lines);

			[Constraints]
			public static partial class OrderSpec
			{
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");

			    public static readonly Constraint<Order> HasLines = Constraints<Order>.Always(o => o.Lines > 0, "no lines");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("(nameof(TotalPositive), TotalPositive)");
		source.Should().Contain("(nameof(HasLines), HasLines)");
	}

	[Test]
	public void ConstraintIgnore_excludes_an_otherwise_eligible_member_from_Constraints_attribute_discovery()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total);

			[Constraints]
			public static partial class OrderSpec
			{
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");

			    // A building block combined into TotalPositive above, not a rule of its own —
			    // [ConstraintIgnore] is the escape hatch for exactly this.
			    [ConstraintIgnore]
			    public static readonly Constraint<Order> NotARuleOfItsOwn = Constraints<Order>.Always(o => true, "unused");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("(nameof(TotalPositive), TotalPositive)");
		source.Should().NotContain("NotARuleOfItsOwn");
	}

	[Test]
	public void Constraints_attribute_makes_any_coexisting_explicit_Constraint_tag_redundant_not_duplicated()
	{
		// A member tagged [Constraint] on a type that also carries [Constraints] is already found
		// by auto-discovery — it must appear exactly once in the generated set, not twice.
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total);

			[Constraints]
			public static partial class OrderSpec
			{
			    [Constraint]
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Split("nameof(TotalPositive)").Length.Should().Be(2, "TotalPositive must be bundled exactly once, not once per attribute that mentions it");
	}

	[Test]
	public void Constraints_attribute_on_a_self_attach_type_still_adds_IHaveConstraints()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			[Constraints]
			public partial record Item(int Value)
			{
			    public static readonly Constraint<Item> Positive = Constraints<Item>.Always(i => i.Value > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("global::Nullean.Entities.IHaveConstraints<Item>");
		source.Should().Contain("(nameof(Positive), Positive)");
	}

	[Test]
	public void Constraints_attribute_on_a_non_partial_type_is_still_refused_with_NECG001()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(decimal Total);

			[Constraints]
			public static class OrderSpec
			{
			    public static readonly Constraint<Order> TotalPositive = Constraints<Order>.Always(o => o.Total > 0, "not positive");
			}
			"""
		);

		run.HasDiagnostic("NECG001").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void Constraints_attribute_with_no_eligible_members_generates_nothing_and_raises_no_diagnostic()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			[Constraints]
			public static partial class EmptySpec
			{
			    public static readonly int NotAConstraint = 1;
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		run.Generated.Should().BeEmpty();
	}
}

public class AnnotationGeneratorShapeTests
{
	[Test]
	public void A_Constraints_type_with_only_annotations_gets_a_generated_set_with_no_reflection()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			[Constraints]
			public partial record Person([Required] string Name, [Range(1, 9)] int Level);
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("global::Nullean.Entities.IHaveConstraints<Person>");
		source.Should().Contain("(\"NameRequired\", global::Nullean.Entities.Constraints<global::Sample.Person>.Always(x => x.Name,");
		source.Should().Contain("(\"LevelRange\"");
		source.Should().NotContain("Validator").And.NotContain("GetCustomAttribute").And.NotContain("typeof(");
		run.OutputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
	}

	[Test]
	public void A_validation_attribute_that_is_not_supported_is_reported_as_info_not_skipped_silently()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			[Constraints]
			public partial record Contact([Required] string Name, [Phone] string Number);
			"""
		);

		var info = run.Diagnostics.Should().ContainSingle(d => d.Id == "NECG010").Which;
		info.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
		info.GetMessage().Should().Contain("Contact.Number").And.Contain("Phone");
		run.SingleGeneratedSource().Should().Contain("NameRequired");
	}

	[Test]
	public void A_type_without_annotations_is_unchanged()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public partial record Item(int Value)
			{
			    [Constraint]
			    public static readonly Constraint<Item> Positive = Constraints<Item>.Always(i => i.Value > 0, "not positive");
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		run.SingleGeneratedSource().Should().NotContain("Annotation").And.Contain("(nameof(Positive), Positive)");
	}

	[Test]
	public void A_name_that_collides_with_a_declared_constraint_gets_an_Annotation_suffix()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			[Constraints]
			public partial record Item([Range(1, 5)] int Value)
			{
			    public static readonly Constraint<Item> ValueRange = Constraints<Item>.Always(i => i.Value != 3);
			}
			"""
		);

		var source = run.SingleGeneratedSource();
		source.Should().Contain("(nameof(ValueRange), ValueRange)").And.Contain("\"ValueRangeAnnotation\"");
	}
}

public class NestedTypeGeneratorShapeTests
{
	[Test]
	public void A_type_nested_in_partial_types_gets_its_set_with_every_container_redeclared_partial()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public static partial class Billing
			{
			    public partial class Documents
			    {
			        public sealed partial record Line(int Quantity)
			        {
			            [Constraint]
			            public static readonly Constraint<Line> Positive = Constraints<Line>.Always(l => l.Quantity > 0);
			        }
			    }
			}
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("partial class Billing").And.Contain("partial class Documents").And.Contain("partial record Line");
		run.OutputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
	}

	[Test]
	public void A_container_that_is_not_partial_still_refuses_with_NECG005_naming_it()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public static class Closed
			{
			    public sealed partial record Line(int Quantity)
			    {
			        [Constraint]
			        public static readonly Constraint<Line> Positive = Constraints<Line>.Always(l => l.Quantity > 0);
			    }
			}
			"""
		);

		run.Diagnostics.Single(d => d.Id == "NECG005").GetMessage().Should().Contain("Closed");
		run.Generated.Should().BeEmpty();
	}
}

public class GenericConstraintsAttributeShapeTests
{
	[Test]
	public void A_Constraints_of_T_companion_generates_a_set_for_T_with_its_annotations()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order([Required] string Number, [Range(1, 9)] int Quantity);

			[Constraints<Order>]
			public static partial class OrderRules;
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		var source = run.SingleGeneratedSource();
		source.Should().Contain("ConstraintSet<global::Sample.Order>").And.Contain("\"NumberRequired\"").And.Contain("\"QuantityRange\"");
		source.Should().Contain("TypeConstraints : global::Nullean.Entities.IConstraintsFor<global::Sample.Order>").And.NotContain("IHaveConstraints");
		run.OutputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
	}

	[Test]
	public void A_member_about_another_type_is_an_inconsistent_type_argument()
	{
		var run = GeneratorTestHost.Run(
			"""
			using Nullean.Entities;

			namespace Sample;

			public sealed record Order(int Quantity);
			public sealed record Customer(string Name);

			[Constraints<Order>]
			public static partial class OrderRules
			{
			    public static readonly Constraint<Customer> NamePresent = Constraints<Customer>.Always(c => c.Name.Length > 0);
			}
			"""
		);

		run.HasDiagnostic("NECG007").Should().BeTrue();
		run.Generated.Should().BeEmpty();
	}

	[Test]
	public void The_type_can_name_itself_and_then_is_self_attached()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.ComponentModel.DataAnnotations;
			using Nullean.Entities;

			namespace Sample;

			[Constraints<Order>]
			public partial record Order([Required] string Number);
			"""
		);

		run.Diagnostics.Should().BeEmpty();
		run.SingleGeneratedSource().Should().Contain("IHaveConstraints<Order>").And.Contain("\"NumberRequired\"");
	}
}
