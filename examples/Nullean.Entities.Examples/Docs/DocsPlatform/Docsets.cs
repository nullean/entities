// A documentation set as docs-builder holds it: a tree of table-of-contents entries identified only by their path, ids of products and a codex group that point
// into other aggregates, and (one layer earlier, before the loader expands them) a graph of toc.yml files that include each other.

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public sealed record TocNode(string Path, IReadOnlyList<TocNode> Children);

public sealed record Docset(string Name, IReadOnlyList<string> Products, string? CodexGroup, IReadOnlyList<TocNode> Toc);

public sealed record CodexGroup(string Id, string Name);

public sealed record CodexConfig(IReadOnlyList<CodexGroup> Groups);

public sealed record DocsetInCatalog(Docset Docset, Catalog Catalog);

public sealed record DocsetInCodex(Docset Docset, CodexConfig Codex);

/// <summary>A toc.yml before the loader expands it: the files it includes, by path. A cycle can only exist here; the expanded tree cannot hold one.</summary>
public sealed record TocFile(string Path, IReadOnlyList<string> Includes);

public sealed record TocIncludes(IReadOnlyList<TocFile> Files);

[Constraints]
public static partial class DocsetConstraints
{
	/// <summary>D74: a file listed twice anywhere in the tree (the first wins today, silently).</summary>
	public static readonly Constraint<Docset> NoFileIsListedTwice = Constraints<Docset>
		.Unique(d => Tree.Flatten(d.Toc, n => n.Children), n => n.Path)
		.Describe("D74");

	/// <summary>The textual `..` check: an entry may not climb out of its folder.</summary>
	public static readonly Constraint<Docset> FilesStayInTheirFolder = Constraints<Docset>
		.Always(
			d => Tree.Flatten(d.Toc, n => n.Children),
			nodes => nodes.All(n => !n.Path.Contains("..", StringComparison.Ordinal)),
			"A toc entry may not contain '..'."
		)
		.Describe("file-stays-in-folder");
}

[Constraints]
public static partial class DocsetInCatalogConstraints
{
	/// <summary>D22: a docset's product ids exist in the catalog. The resolver silently drops unknown ones today.</summary>
	public static readonly Constraint<DocsetInCatalog> ProductsExist = Constraints<DocsetInCatalog>
		.References<string, string>(p => p.Docset.Products, id => [id], p => p.Catalog.Products.Select(x => x.Id))
		.Describe("I2/D22");
}

[Constraints]
public static partial class DocsetInCodexConstraints
{
	/// <summary>D29/P55: a docset's codex group is a group the codex declares. Only passed through today, never checked.</summary>
	public static readonly Constraint<DocsetInCodex> CodexGroupExists = Constraints<DocsetInCodex>
		.Check(p =>
				p.Docset.CodexGroup is null || p.Codex.Groups.Any(g => g.Id == p.Docset.CodexGroup)
					? []
					: [
						new Violation($"The codex group '{p.Docset.CodexGroup}' is not declared.")
						{
							Member = "Docset.CodexGroup",
							Evidence = p.Docset.CodexGroup
						}
					])
		.Describe("D29");
}

[Constraints]
public static partial class TocIncludesConstraints
{
	/// <summary>D69: toc.yml files do not include each other in a loop. The developer docs claim this is caught; the loader catches it while expanding, the domain never sees it.</summary>
	public static readonly Constraint<TocIncludes> IncludesAreAcyclic = Constraints<TocIncludes>
		.Acyclic(t => t.Files, f => f.Path, f => f.Includes)
		.Describe("D69");

	public static readonly Constraint<TocIncludes> IncludesExist = Constraints<TocIncludes>
		.References<TocFile, string>(t => t.Files, f => f.Includes, t => t.Files.Select(x => x.Path))
		.Describe("An include names a toc.yml that exists.");
}
