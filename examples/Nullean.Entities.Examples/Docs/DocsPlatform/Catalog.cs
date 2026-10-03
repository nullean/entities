// A documentation platform's global catalog, modelled the way docs-builder's `erd-model` branch models it: products with a versioning system and a lifecycle.
// The ids in the descriptions (G87, V1, G89...) are the constraint ids of the docs-eng-team domain-model investigation (docs/domain-model/constraints.md).

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public enum ProductLifecycle
{
	Preview,
	Beta,
	Ga,
	Deprecated,
	Removed
}

public sealed record Product(string Id, string Name, ProductLifecycle Lifecycle, string VersioningSystemId)
{
	public Product Promote() => Lifecycle switch
	{
		ProductLifecycle.Preview => this with { Lifecycle = ProductLifecycle.Beta },
		ProductLifecycle.Beta => this with { Lifecycle = ProductLifecycle.Ga },
		_ => this,
	};

	public Product Deprecate() => Lifecycle == ProductLifecycle.Ga ? this with { Lifecycle = ProductLifecycle.Deprecated } : this;

	public Product Remove() => Lifecycle == ProductLifecycle.Deprecated ? this with { Lifecycle = ProductLifecycle.Removed } : this;
}

public sealed record VersioningSystem(string Id, Version Base, Version Current);

public sealed record Catalog(IReadOnlyList<Product> Products, IReadOnlyList<VersioningSystem> VersioningSystems);

// A product's lifecycle declared as data. The investigation found no rule anywhere that orders GA, preview, deprecated and removed; this is that rule.
public static class ProductMachine
{
	public static readonly StateMachine<ProductLifecycle, string> Machine = StateMachine
		.Define<ProductLifecycle, string>(ProductLifecycle.Preview)
		.Permit("Promote", ProductLifecycle.Preview, ProductLifecycle.Beta)
		.Permit("Promote", ProductLifecycle.Beta, ProductLifecycle.Ga)
		.Permit("Deprecate", ProductLifecycle.Ga, ProductLifecycle.Deprecated)
		.Permit("Remove", ProductLifecycle.Deprecated, ProductLifecycle.Removed)
		.Build();
}

[Constraints]
public static partial class ProductConstraints
{
	public static readonly Constraint<Product> LegalLifecycle = ProductMachine.Machine.AsConstraint<Product>(p => p.Lifecycle);

	public static readonly Constraint<Product> ProductIsNamed = Constraints<Product>.Always(
		p => p.Name,
		name => !string.IsNullOrWhiteSpace(name),
		"A product has a name."
	);
}

[Constraints]
public static partial class CatalogConstraints
{
	/// <summary>G87: product ids are unique, case-insensitively (they become keys and URLs).</summary>
	public static readonly Constraint<Catalog> ProductIdsAreUnique = Constraints<Catalog>
		.Unique(c => c.Products, p => p.Id, StringComparer.OrdinalIgnoreCase)
		.Describe("G87: duplicate product keys that differ only by case collide.");

	/// <summary>V1, G80: a product's versioning system exists in the catalog. (The loader silently dropped unknown ones today: D22.)</summary>
	public static readonly Constraint<Catalog> VersioningSystemsExist = Constraints<Catalog>
		.References<Product, string>(c => c.Products, p => [p.VersioningSystemId], c => c.VersioningSystems.Select(v => v.Id))
		.Describe("V1/G80: every product names a versioning system that exists.");

	/// <summary>G89: a versioning system's base is not after its current version. Docs-builder only reports a hint today.</summary>
	public static readonly Constraint<Catalog> BaseIsNotAfterCurrent = Constraints<Catalog>
		.Always(c => c.VersioningSystems, systems => systems.All(v => v.Base <= v.Current), "A versioning system's base must not be after its current version.")
		.Describe("G89");
}
