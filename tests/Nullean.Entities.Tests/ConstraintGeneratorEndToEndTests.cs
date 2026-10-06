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

// ── Static properties: the property name is the id, kebab-cased ──────────────────

internal sealed record Stock(int OnHand, int Reserved);

[Constraints]
internal static partial class StockRules
{
	public static Constraint<Stock> OnHandIsNotNegative { get; } = Constraints<Stock>.Always(s => s.OnHand >= 0, "negative stock");

	public static Constraint<Stock> ReservedWithinOnHand { get; } = Constraints<Stock>.Always(s => s.Reserved <= s.OnHand, "over-reserved").Describe(
		"Reserved stock exists."
	);
}

internal sealed record Docset(IReadOnlyList<string> CrossLinks, IReadOnlyList<string> Products);

internal sealed record RejectedProduct(string Name);

[Constraints]
internal static partial class DocsetRules
{
	// The rule's evidence is the offending text itself.
	public static Constraint<Docset, string> CrossLinkWellFormed { get; } =
		Constraints<Docset>.Each(d => d.CrossLinks, link => link.Contains("://") ? null : $"'{link}' has no registry").Describe("Cross links name a registry.");

	// A warning never rejects anything.
	public static Constraint<Docset, string> ProductsLowerCase { get; } =
		Constraints<Docset>.Each(d => d.Products, p => p == p.ToLowerInvariant() ? null : $"'{p}' is not lower case").WithSeverity(Severity.Warning);

	// The evidence is a record the rule builds, not the item.
	public static Constraint<Docset, RejectedProduct> ProductKnown { get; } =
		Constraints<Docset>.EachCheck<string, RejectedProduct>(
			d => d.Products,
			(_, p) => p.StartsWith('x') ? [new Violation($"unknown product {p}") { Evidence = new RejectedProduct(p) }] : []
		);
}

public class TypedEvidenceTests
{
	private static readonly Docset Sample = new([" kibana://a ", "oops", "es://b"], ["kibana", "Xray", "xyz"]);

	[Test]
	public void A_generated_set_accepts_typed_rule_members_and_names_them()
	{
		DocsetRules.All.AsConstraints().Select(c => c.Name).Should().Equal("cross-link-well-formed", "products-lower-case", "product-known");
		DocsetRules.CrossLinkWellFormed.Name.Should().Be("cross-link-well-formed");
		DocsetRules.CrossLinkWellFormed.Description.Should().Be("Cross links name a registry.");
	}

	[Test]
	public void Keep_drops_what_the_rule_rejected_and_returns_the_original_items_in_order()
	{
		var report = DocsetRules.All.Report(Sample);

		report.Keep(Sample.CrossLinks, DocsetRules.CrossLinkWellFormed).Should().Equal(" kibana://a ", "es://b");
		report.Rejected(DocsetRules.CrossLinkWellFormed).Should().BeEquivalentTo(["oops"]);
	}

	[Test]
	public void Keep_can_derive_the_evidence_from_the_item()
	{
		var report = DocsetRules.All.Report(new Docset([], ["kibana", "xray", "xyz"]));

		report.Keep(["kibana", "xray", "xyz"], DocsetRules.ProductKnown, p => new RejectedProduct(p)).Should().Equal("kibana");
		report.Rejected(DocsetRules.ProductKnown).Should().BeEquivalentTo([new RejectedProduct("xray"), new RejectedProduct("xyz")]);
	}

	[Test]
	public void A_warning_never_rejects_an_item_and_the_report_still_has_it()
	{
		var report = DocsetRules.All.Report(Sample);

		report.Rejected(DocsetRules.ProductsLowerCase).Should().BeEmpty();
		report.Keep(Sample.Products, DocsetRules.ProductsLowerCase).Should().Equal(Sample.Products);
		report.Of(DocsetRules.ProductsLowerCase).Should().ContainSingle().Which.Severity.Should().Be(Severity.Warning);
		report.HasErrors.Should().BeTrue("the cross-link and product rules are errors");
		DocsetRules.All.Report(new Docset([], ["Kibana"])).HasErrors.Should().BeFalse("a warning alone does not make the value unusable");
	}

	[Test]
	public void Of_matches_by_the_rule_not_by_an_id_string()
	{
		var report = DocsetRules.All.Report(Sample);

		report.Of(DocsetRules.CrossLinkWellFormed).Should().ContainSingle().Which.ConstraintId.Should().Be("cross-link-well-formed");
		report.Errors.Select(v => v.ConstraintId).Should().Contain(["cross-link-well-formed", "product-known"]);
	}
}

public class StaticPropertyConstraintsTests
{
	[Test]
	[Arguments("PathPrefixesUnique", "path-prefixes-unique")]
	[Arguments("URLPath", "url-path")]
	[Arguments("EntryTitleMax80", "entry-title-max-80")]
	[Arguments("Under100", "under-100")]
	[Arguments("Rule_Two", "rule-two")]
	public void ConstraintName_makes_kebab_case_ids(string member, string id) => ConstraintName.Of(member).Should().Be(id);

	[Test]
	public void Auto_properties_are_the_rules_and_their_kebab_case_names_are_the_ids()
	{
		StockRules.All.AsConstraints().Select(c => c.Name).Should().Equal("on-hand-is-not-negative", "reserved-within-on-hand");
		StockRules
			.All
			.AsConstraints()
			.Select(c => c.Name)
			.Should()
			.Equal(ConstraintName.Of(nameof(StockRules.OnHandIsNotNegative)), ConstraintName.Of(nameof(StockRules.ReservedWithinOnHand)));
		StockRules.All.Validate(new Stock(1, 5)).Single().ConstraintId.Should().Be("reserved-within-on-hand");
		StockRules.All.AsConstraints()[1].Description.Should().Be("Reserved stock exists.");
	}
}

public class SelfAttachedConstraintsTests
{
	[Test]
	public void GeneratedType_implements_IHaveConstraints() => GeneratedItem.Constraints.Should().BeAssignableTo<ConstraintSet<GeneratedItem>>();

	[Test]
	public void Generated_Constraints_bundles_every_Constraint_field()
	{
		var violations = GeneratedItem.Constraints.Validate(new GeneratedItem(-1));
		violations.Should().ContainSingle(v => v.ConstraintId == "positive");
	}

	[Test]
	public void Generated_Constraints_reflects_every_field_not_just_the_first()
	{
		var violations = GeneratedItem.Constraints.Validate(new GeneratedItem(200));
		violations.Should().ContainSingle(v => v.ConstraintId == "under-100");
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
		violations.Select(v => v.ConstraintId).Should().Contain(["total-non-negative", "payment-ref-required"]);
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
