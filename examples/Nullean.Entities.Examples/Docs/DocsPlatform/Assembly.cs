// The assembled site, in docs-builder's own shapes: repositories in a dictionary keyed by their name (the key duplicates the entity's own name), and navigation mounts that
// refer to a repository by the scheme or path of a URI, never by an id. Rules that need both halves are written over a pair record, the way docs-builder's own
// `DocumentationSetInCatalog` is.

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public sealed record Repository(string Name);

public sealed record Assembly(IReadOnlyDictionary<string, Repository> Repositories);

public sealed record Mount(Uri Source, string PathPrefix);

public sealed record Navigation(IReadOnlyList<Mount> Mounts);

/// <summary>The pair a rule over two aggregates is written against.</summary>
public sealed record NavigationInAssembly(Navigation Navigation, Assembly Assembly);

public static class MountUri
{
	/// <summary>docs-builder mounts by <c>repo://path</c>; the repository is read from the URI, here its first path segment.</summary>
	public static Uri Make(string repository) => new($"docs://site/{Uri.EscapeDataString(repository)}");

	public static string Repository(Mount mount) => Uri.UnescapeDataString(mount.Source.AbsolutePath.TrimStart('/'));
}

[Constraints]
public static partial class AssemblyConstraints
{
	/// <summary>The dictionary key duplicates <c>Repository.Name</c>; nothing keeps the two equal.</summary>
	public static readonly Constraint<Assembly> RepositoryKeysMatchNames = Constraints<Assembly>
		.KeysMatch(a => a.Repositories, r => r.Name)
		.Describe("A repository is stored under its own name.");
}

[Constraints]
public static partial class NavigationConstraints
{
	/// <summary>G67: mounts have distinct path prefixes.</summary>
	public static readonly Constraint<Navigation> PathPrefixesAreUnique = Constraints<Navigation>.Unique(n => n.Mounts, m => m.PathPrefix).Describe("G67");
}

[Constraints]
public static partial class NavigationInAssemblyConstraints
{
	/// <summary>A mount refers to a repository the assembly knows. Nothing checks it in the domain today; the navigation build throws "Could not find navigation node".</summary>
	public static readonly Constraint<NavigationInAssembly> MountIsARepository = Constraints<NavigationInAssembly>
		.References<Mount, string>(p => p.Navigation.Mounts, m => [MountUri.Repository(m)], p => p.Assembly.Repositories.Keys)
		.Describe("G62/I6: every mount names a repository that exists.");
}
