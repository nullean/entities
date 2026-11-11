---
name: nullean-entities-filesystem
description: >-
  Use when a model holds IFileInfo or IDirectoryInfo paths and you need to check that they exist, stay inside a root folder, avoid symlinks and hidden folders, or have the right extension. Nullean.Entities.FileSystem gives Files<T> constraint shapes over System.IO.Abstractions that report every offending path as a named violation.
---

# Nullean.Entities.FileSystem

`Files<T>` turns file rules into ordinary `Constraint<T>` values from `Nullean.Entities`, and `FilePaths` exposes the same path arithmetic for hand-written checks. To generate hostile file trees against these rules in tests, add `Nullean.Entities.CsCheck.FileSystem`.

## The calls you need

```csharp
using System.IO.Abstractions;
using Nullean.Entities;
using Nullean.Entities.FileSystem;

[Constraints]
public static partial class DocsetConstraints
{
	public static readonly Constraint<Docset> ImagesExist = Files<Docset>.Exist(d => d.Images);
	public static readonly Constraint<Docset> ImagesStayInRoot = Files<Docset>.StayInside(d => d.Root, d => d.Images);
	public static readonly Constraint<Docset> NoSymlinks = Files<Docset>.NotSymlink(d => d.Images, d => d.Root);
	public static readonly Constraint<Docset> Pictures = Files<Docset>.HaveExtension(d => d.Images, [".png", ".svg"]);
}
```

```csharp
IFileSystem fs = new FileSystem(); // a MockFileSystem in tests
var docset = new Docset(fs.DirectoryInfo.New("/docs"), [fs.FileInfo.New("/docs/../etc/passwd")]);
foreach (var v in DocsetConstraints.All.Validate(docset))
	Console.WriteLine($"{v.ConstraintId} ({v.Member}): {v.Message}");
```

Items that only name a file (a TOC entry, a redirect):

```csharp
var tocTargetsExist = Files<Toc>.Exist(
	t => t.Entries,
	(t, e) => t.Root.FileSystem.FileInfo.New(Path.Join(t.Root.FullName, e.Path)),
	describe: e => $"TOC entry '{e.Path}' points at a missing file.");
```

Inside a loader:

```csharp
if (FilePaths.UnsafeToRead(file, root) is { } reason)
	throw new InvalidOperationException($"Refusing to read '{file.FullName}': {reason}.");
```

## Gotchas

- Rules read the path's own `IFileSystemInfo.FileSystem`: build the model on a `MockFileSystem` in tests.
- A path that throws (IO, argument, null) becomes one "could not check the path" violation of that rule; `Validate` does not throw.
- `StayInside` and `NoHiddenSegments` take `(root, paths)`; `NotSymlink` takes `(paths, root)`, and its root is optional.
- Comparisons are ordinal, case-sensitive even on macOS and Windows. Only the `FilePaths` helpers take a `StringComparison`.
- `NotEmpty` and `MaxSize` skip a missing file; pair them with `Exist`.
- `HaveExtension` wants the dot (`".png"`).
- The item overloads (`Exist<TItem>`, `DirectoriesExist<TItem>`, `NotEmpty<TItem>`, `UniqueIgnoringCase<TItem>`) have no `message:` parameter; word findings with `describe:`.
- `Violation.Member` is the selector text (`"Images"`), and `Evidence` is the full path.

Docs: https://entities.nullean.net
