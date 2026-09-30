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
	public void Companion_class_case_gets_the_property_but_not_the_interface()
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
		source.Should().Contain("global::Nullean.Entities.ConstraintSet<global::Sample.Order>");
	}

	[Test]
	public void Reactive_case_bundles_into_a_ReactiveConstraintSet_with_named_tuples()
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
}
