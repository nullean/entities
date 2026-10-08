# Nullean.Entities.FileSystem

Constraint shapes for files and directories, over the `System.IO.Abstractions` types a domain model already holds (`IFileInfo`, `IDirectoryInfo`).

```csharp
[Constraints]
public static partial class DocsetConstraints
{
    public static readonly Constraint<Docset> ImagesExist       = Files<Docset>.Exist(d => d.Images);
    public static readonly Constraint<Docset> ImagesStayInRoot  = Files<Docset>.StayInside(d => d.Root, d => d.Images);
    public static readonly Constraint<Docset> NoSymlinks        = Files<Docset>.NotSymlink(d => d.Images, d => d.Root);
    public static readonly Constraint<Docset> NoHiddenFolders   = Files<Docset>.NoHiddenSegments(d => d.Root, d => d.Images);
    public static readonly Constraint<Docset> PictureExtensions = Files<Docset>.HaveExtension(d => d.Images, [".png", ".svg"]);
}
```

| Shape | Violated when |
|---|---|
| `Exist`, `DirectoriesExist` | a file or directory is missing (evidence: its full path) |
| `StayInside(root, paths)` | a path, once `..` is resolved, is not inside the root. A sibling that shares a prefix (`/docs-other` against `/docs`) is outside |
| `NotSymlink(paths, root?)` | a path is a symbolic link, or sits below one (checked up to the root) |
| `NoHiddenSegments(root, paths)` | a directory between the root and the path starts with a dot |
| `HaveExtension(paths, extensions)` | the extension is not one of the list, ignoring case |
| `NotEmpty(files)`, `MaxSize(files, bytes)` | an existing file has no content, or is larger than the limit (a missing file is `Exist`'s to report) |
| `UniqueIgnoringCase(paths)` | two paths differ only in case: two files on Linux, one on macOS and Windows |

Each takes `message:` (one text for every finding), `describe:` (a text per path) and `at:` (a `SourceLocation` for the item, so a finding can point at the YAML line), and reports the selected member as `Violation.Member`. Use `.WithSeverity(Severity.Warning)` for a rule that is not an error yet.

When the model's items are not files (a table-of-contents entry, a redirect target), `Exist` and `DirectoriesExist` take the items and a function that resolves each to the file it names; `describe` and `at` then speak about the item:

```csharp
Files<Docset>.DirectoriesExist(
    d => d.Folders,                                   // the entries
    (d, f) => d.FileSystem.DirectoryInfo.New(Path.Join(d.Root.FullName, f.Path)),
    describe: f => $"Folder '{f.Path}' does not exist.",
    at: f => new SourceLocation(f.File, f.Line, f.Column, f.Path.Length));
```

`FilePaths` is the path arithmetic the shapes use, public for a rule that needs the answer inside a larger check: `IsInside(root, path, comparison)`, `SymlinkAt`, `HiddenSegment`, and
`UnsafeToRead(path, root)`, the reason a path may not be read as content (a link, or a hidden directory, on the way) or null. A chain that stops at the first problem (right extension, inside the root, safe to read, exists) keeps its own
wording and asks `FilePaths` instead of re-deriving the rules.
The rules read the file system the path came from (`IFileSystemInfo.FileSystem`), so a test that builds the model on a `MockFileSystem` checks the very code that runs against the disk. A path the
file system refuses is reported as a violation of that rule, not thrown.

The package is trim and AOT clean: `./build.sh aot` publishes an app using these rules NativeAOT. To get counter-examples for them with no hand-written file system, see
[`Nullean.Entities.CsCheck.FileSystem`](../Nullean.Entities.CsCheck.FileSystem/README.md).
