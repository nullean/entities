// Real end-to-end coverage for [Constraint]: these fixtures are compiled by the actual generator
// (wired in as an analyzer on this test project too — see Nullean.Entities.Tests.csproj), not just
// asserted against generated source text. If the generator regresses in a way that still compiles
// but produces the wrong runtime behaviour, a source-text-only test could miss it; these can't.

using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// ── Self-attach: the domain type itself gets IHaveConstraints<T> ───────────────

internal sealed partial record GeneratedItem(int Value)
{
	[Constraint]
	public static readonly Constraint<GeneratedItem> Positive = Constraints<GeneratedItem>.Always(i => i.Value > 0, "not positive");

	[Constraint]
	public static readonly Constraint<GeneratedItem> Under100 = Constraints<GeneratedItem>.Check(i => i.Value <= 100 ? [] : [Violation.Warning("exceeds 100")]);
}

public class SelfAttachedConstraintsTests
{
	[Test]
	public void GeneratedType_implements_IHaveConstraints() => GeneratedItem.Constraints.Should().BeAssignableTo<ConstraintSet<GeneratedItem>>();

	[Test]
	public void Generated_Constraints_bundles_every_Constraint_field()
	{
		var violations = GeneratedItem.Constraints.Validate(new GeneratedItem(-1));
		violations.Should().ContainSingle(v => v.ConstraintId == "Positive");
	}

	[Test]
	public void Generated_Constraints_reflects_every_field_not_just_the_first()
	{
		var violations = GeneratedItem.Constraints.Validate(new GeneratedItem(200));
		violations.Should().ContainSingle(v => v.ConstraintId == "Under100");
	}

	[Test]
	public void IHaveConstraints_interface_is_actually_implemented()
	{
		// Only compiles if the generator really added `: IHaveConstraints<GeneratedItem>` to the
		// partial declaration — this is the interface-conformance half of the contract, not just
		// "there happens to be a static property with this name".
		static ConstraintSet<T> ConstraintsOf<T>() where T : IHaveConstraints<T> => T.Constraints;

		ConstraintsOf<GeneratedItem>().Should().BeSameAs(GeneratedItem.Constraints);
	}
}

// ── Companion class: T is untouched, the spec class gets the plain property ────

internal sealed record GeneratedOrder(Guid Id, decimal Total, string? PaymentRef);

internal static partial class GeneratedOrderSpec
{
	[Constraint]
	public static readonly Constraint<GeneratedOrder> TotalNonNegative = Constraints<GeneratedOrder>.Always(o => o.Total >= 0, "negative total");

	[Constraint]
	public static readonly Constraint<GeneratedOrder> PaymentRefRequired = Constraints<GeneratedOrder>.Always(o => o.PaymentRef != null, "missing payment ref");
}

public class CompanionClassConstraintsTests
{
	// GeneratedOrder itself is untouched: there is no runtime check that proves it, because the
	// property this test suite would need to check doesn't even parse otherwise. IHaveConstraints
	// is F-bounded (`where TSelf : IHaveConstraints<TSelf>`), so writing the type
	// `IHaveConstraints<GeneratedOrder>` at all — even just to reflect over it — is itself a
	// compile error unless GeneratedOrder already implements it. If the generator ever mistakenly
	// added that interface to the companion-class case, this whole file would fail to build long
	// before any assertion ran.

	[Test]
	public void Spec_class_bundles_both_fields()
	{
		var order = new GeneratedOrder(Guid.NewGuid(), -5m, null);
		var violations = GeneratedOrderSpec.All.Validate(order);

		violations.Should().HaveCount(2);
		violations.Select(v => v.ConstraintId).Should().Contain(["TotalNonNegative", "PaymentRefRequired"]);
	}

	[Test]
	public void Spec_class_reports_no_violations_for_a_valid_order()
	{
		var order = new GeneratedOrder(Guid.NewGuid(), 10m, "pay-1");
		GeneratedOrderSpec.All.Validate(order).Should().BeEmpty();
	}

	[Test]
	public void IConstraintsFor_interface_is_actually_implemented_via_the_nested_TypeConstraints()
	{
		// IConstraintsFor<T> is a plain instance interface (not static-abstract, not F-bounded), so
		// generic code can take a value implementing it directly and infer T from the argument —
		// no separate TSpec type parameter needed, and no need to know GeneratedOrderSpec exists.
		static ConstraintSet<T> ConstraintsFor<T>(IConstraintsFor<T> spec) => spec.All;

		ConstraintsFor(GeneratedOrderSpec.TypeConstraints.Instance).Should().BeSameAs(GeneratedOrderSpec.All);
	}

	[Test]
	public void Instance_is_a_forwarding_shortcut_for_TypeConstraints_Instance()
	{
		// GeneratedOrderSpec.Instance is the generated top-level shortcut — callers never have to
		// spell out "TypeConstraints" to get the IConstraintsFor<Order> value.
		GeneratedOrderSpec.Instance.Should().BeEquivalentTo(GeneratedOrderSpec.TypeConstraints.Instance);
		GeneratedOrderSpec.Instance.All.Should().BeSameAs(GeneratedOrderSpec.All);
	}
}
