// What one repository publishes (pages and the redirects for pages that moved) and how the assembled site mounts tables of contents: the
// uniqueness, referential-integrity and graph rules the docs-eng-team investigation lists as unchecked or checked late.

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public sealed record Page(string Url);

public sealed record Redirect(string From, string To);

public sealed record DocumentationSet(string Repository, IReadOnlyList<Page> Pages, IReadOnlyList<Redirect> Redirects);

/// <summary>A table of contents (a toc.yml) and the ids of the tables of contents it includes.</summary>
public sealed record Toc(string Id, IReadOnlyList<string> IncludedIds);

public sealed record Mount(string PathPrefix, string TocId);

public sealed record Site(IReadOnlyList<Mount> Mounts, IReadOnlyList<Toc> Tocs);

[Constraints]
public static partial class DocumentationSetConstraints
{
	/// <summary>N1: two pages at one URL silently overwrite each other (foo.md and foo/index.md).</summary>
	public static readonly Constraint<DocumentationSet> PageUrlsAreUnique = Constraints<DocumentationSet>
		.Unique(d => d.Pages, p => p.Url)
		.Describe("N1/I7: page URL uniqueness. Nothing checks it today.");

	/// <summary>D101: a second redirect from one source throws today, naming the wrong file.</summary>
	public static readonly Constraint<DocumentationSet> RedirectSourcesAreUnique = Constraints<DocumentationSet>
		.Unique(d => d.Redirects, r => r.From)
		.Describe("D101");

	/// <summary>D106: a redirect's target is a page that exists.</summary>
	public static readonly Constraint<DocumentationSet> RedirectTargetsExist = Constraints<DocumentationSet>
		.References<Redirect, string>(d => d.Redirects, r => [r.To], d => d.Pages.Select(p => p.Url))
		.Describe("D106");

	/// <summary>N30: A to B while B redirects to C is two hops for every reader; flatten it to A to C.</summary>
	public static readonly Constraint<DocumentationSet> RedirectsAreNotChained = Constraints<DocumentationSet>
		.NoChains(d => d.Redirects, r => r.From, r => r.To)
		.Describe("N30: redirect chains are not followed, so they are not checked either.");

	/// <summary>N25: a redirect from a page that still exists silently wins over that page.</summary>
	public static readonly Constraint<DocumentationSet> RedirectsDoNotShadowPages = Constraints<DocumentationSet>
		.Check(d =>
		{
			var live = d.Pages.Select(p => p.Url).ToHashSet();
			return [
				.. d
					.Redirects
					.Where(r => live.Contains(r.From) && r.From != r.To)
					.Select(r => Violation.Error($"'{r.From}' is redirected away but is still a page."))
			];
		})
		.Describe("N25");
}

[Constraints]
public static partial class SiteConstraints
{
	/// <summary>G67: mounts have distinct path prefixes.</summary>
	public static readonly Constraint<Site> PathPrefixesAreUnique = Constraints<Site>.Unique(s => s.Mounts, m => m.PathPrefix).Describe("G67");

	/// <summary>G62/I6: a mount points at a table of contents that exists.</summary>
	public static readonly Constraint<Site> MountsPointAtTocs = Constraints<Site>
		.References<Mount, string>(s => s.Mounts, m => [m.TocId], s => s.Tocs.Select(t => t.Id))
		.Describe("G62/I6");

	/// <summary>D69: tables of contents do not include each other in a loop. The developer docs claim this is caught; it is not.</summary>
	public static readonly Constraint<Site> TocsAreAcyclic = Constraints<Site>
		.Acyclic(s => s.Tocs, t => t.Id, t => t.IncludedIds)
		.Describe("D69: no cycle detection between toc.yml files.");

	/// <summary>An included table of contents exists.</summary>
	public static readonly Constraint<Site> IncludesPointAtTocs = Constraints<Site>
		.References<Toc, string>(s => s.Tocs, t => t.IncludedIds, s => s.Tocs.Select(t => t.Id))
		.Describe("An include names a toc that exists.");
}
