// What one repository publishes: pages, and the redirects for pages that moved. The uniqueness, referential-integrity and chain rules the
// docs-eng-team investigation lists as unchecked or checked late.

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public sealed record Page(string Url);

public sealed record Redirect(string From, string To);

public sealed record DocumentationSet(string Repository, IReadOnlyList<Page> Pages, IReadOnlyList<Redirect> Redirects);

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
