using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// The required members of the types a type holds are rules of its set too, without an attribute on the nested type.
public class NestedRequiredTests
{
	[Test]
	public void ARequiredMemberOfANestedObject_IsARule()
	{
		var site = new NestedSite
		{
			Name = "docs",
			Owner = new NestedOwner { Login = null! },
			Environments = new Dictionary<string, NestedEnvironment>(),
			Pages = []
		};

		NestedSiteRules.All.Validate(site).Select(v => v.ConstraintId).Should().Equal("owner-login-required");
	}

	[Test]
	public void ARequiredMemberOfEachListItem_IsARule()
	{
		var site = new NestedSite
		{
			Name = "docs",
			Environments = new Dictionary<string, NestedEnvironment>(),
			Pages = [new NestedPage { Title = "ok" }, new NestedPage { Title = null! }]
		};

		NestedSiteRules.All.Validate(site).Select(v => v.ConstraintId).Should().Equal("pages-title-required");
	}

	[Test]
	public void ARequiredMemberOfEachDictionaryValue_IsARule()
	{
		var site = new NestedSite
		{
			Name = "docs",
			Pages = [],
			Environments = new Dictionary<string, NestedEnvironment> { ["prod"] = new() { Uri = "https://example.org" }, ["dev"] = new() { Uri = null! } }
		};

		NestedSiteRules.All.Validate(site).Select(v => v.ConstraintId).Should().Equal("environments-uri-required");
	}

	[Test]
	public void ANestedObjectInsideANestedObject_IsReachedNullSafely()
	{
		var site = new NestedSite
		{
			Name = "docs",
			Pages = [],
			Environments = new Dictionary<string, NestedEnvironment>(),
			Owner = new NestedOwner { Login = "x", Address = new NestedAddress { City = null! } }
		};

		NestedSiteRules.All.Validate(site).Select(v => v.ConstraintId).Should().Equal("owner-address-city-required");
	}

	[Test]
	public void AnAbsentHolder_SaysNothingAboutItsMembers()
	{
		var site = new NestedSite { Name = "docs", Pages = [], Environments = new Dictionary<string, NestedEnvironment>() };

		NestedSiteRules.All.Validate(site).Should().BeEmpty();
	}

	[Test]
	public void TheNestedRules_ArePrerequisites_SoOtherRulesNeverReadTheHole()
	{
		NestedSiteRules.All.AsConstraints().Where(c => c.Name!.EndsWith("-required", StringComparison.Ordinal)).Should().OnlyContain(c => c.IsPrerequisite);
		NestedSiteRules.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo([
			"name-required",
			"pages-required",
			"environments-required",
			"owner-login-required",
			"owner-address-city-required",
			"pages-title-required",
			"environments-uri-required"
		]);
	}

	[Test]
	public void ANestedTypeWithItsOwnSet_IsLeftToThatSet_SoAHoleIsNotReportedTwice()
	{
		ParcelRules.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["id-required", "sender-required"]);
		PartyRules.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["name-required"]);
	}

	[Test]
	public void AComputedView_AndThePositionalPartsOfAPair_AreNotLookedInto()
	{
		// the view holds the same objects as Entries: one rule, not two
		ShelfRules.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["entries-required", "entries-label-required"]);
		// a pair is made of parts that have their own rules
		PairRules.All.AsConstraints().Should().BeEmpty();
	}

	[Test]
	public void ImplicitRequiredOff_TurnsTheNestedRulesOffToo()
	{
		NestedSiteRulesOff.All.AsConstraints().Should().BeEmpty();
	}
}

public sealed class NestedSite
{
	public required string Name { get; init; }
	public NestedOwner? Owner { get; init; }
	public required IReadOnlyList<NestedPage> Pages { get; init; }
	public required IReadOnlyDictionary<string, NestedEnvironment> Environments { get; init; }
}

public sealed class NestedOwner
{
	public required string Login { get; init; }
	public NestedAddress? Address { get; init; }
}

public sealed class NestedAddress
{
	public required string City { get; init; }
}

public sealed class NestedPage
{
	public required string Title { get; init; }
}

public sealed class NestedEnvironment
{
	public required string Uri { get; init; }
}

[Constraints<NestedSite>]
public static partial class NestedSiteRules;

[Constraints<NestedSite>(ImplicitRequired = false)]
public static partial class NestedSiteRulesOff;

public sealed class Parcel
{
	public required string Id { get; init; }
	public required Party Sender { get; init; }
}

public sealed class Party
{
	public required string Name { get; init; }
}

[Constraints<Parcel>]
public static partial class ParcelRules;

[Constraints<Party>]
public static partial class PartyRules;

public sealed class ShelfEntry
{
	public required string Label { get; init; }
}

public sealed class Shelf
{
	public required IReadOnlyDictionary<string, ShelfEntry> Entries { get; init; }

	public IReadOnlyDictionary<string, ShelfEntry> Labelled =>
		field ??= Entries.Where(e => e.Value.Label.Length > 0).ToDictionary();
}

public sealed record Pair(Party Left, Party Right);

[Constraints<Shelf>]
public static partial class ShelfRules;

[Constraints<Pair>]
public static partial class PairRules;
