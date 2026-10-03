using System.IO.Abstractions;
using Nullean.Entities;
using Nullean.Entities.FileSystem;

namespace DocsSiteBuild;

/// <summary>A documentation set: a title and the files it names. The files are live handles, so the rules read the file system they came from.</summary>
public sealed record Docset(
	string Title,
	IFileSystem FileSystem,
	IDirectoryInfo Root,
	IFileInfo SourceFile,
	IReadOnlyList<IFileInfo> Pages,
	IReadOnlyList<IFileInfo> Images
);

/// <summary>A docset and the line of <c>docset.yml</c> each of its files was named on, so a finding can point there.</summary>
public sealed record LoadedDocset(Docset Docset, IReadOnlyDictionary<string, int> Lines);

[Constraints]
public static partial class DocsetRules
{
	public static readonly Constraint<Docset> TitleIsRequired = Constraints<Docset>.Always(d => !string.IsNullOrWhiteSpace(d.Title), "A docset needs a title");

	public static readonly Constraint<Docset> SourceFileExists = Files<Docset>.Exist(d => [d.SourceFile]);

	public static readonly Constraint<Docset> PagesExist = Files<Docset>.Exist(d => d.Pages);

	public static readonly Constraint<Docset> ImagesExist = Files<Docset>.Exist(d => d.Images);

	// `..` is resolved first, so `img/../../secret.png` is outside; a sibling that shares a prefix (`/docs-other` for `/docs`) is outside too.
	public static readonly Constraint<Docset> FilesStayInTheDocset = Files<Docset>.StayInside(d => d.Root, d => d.Pages.Concat(d.Images));

	public static readonly Constraint<Docset> NoSymlinks = Files<Docset>.NotSymlink(d => d.Pages.Concat(d.Images), d => d.Root);

	public static readonly Constraint<Docset> NoHiddenFolders = Files<Docset>.NoHiddenSegments(d => d.Root, d => d.Pages.Concat(d.Images));

	public static readonly Constraint<Docset> PagesAreMarkdown = Files<Docset>.HaveExtension(d => d.Pages, [".md"]);

	public static readonly Constraint<Docset> ImagesArePictures = Files<Docset>.HaveExtension(d => d.Images, [".png", ".svg"]);

	// a file named twice would be built twice and one copy would overwrite the other
	public static readonly Constraint<Docset> PagesAreListedOnce = Constraints<Docset>.Unique(
		d => d.Pages,
		p => p.FullName,
		message: "A page is listed more than once"
	);
}
