# Nullean.Entities.FileSystem

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.FileSystem.svg)](https://www.nuget.org/packages/Nullean.Entities.FileSystem)

**Catch the missing image, the `../../secret.png` path and the symlink that escapes your content folder with named rules, before they reach a build or a server.**

A model that holds `IFileInfo` and `IDirectoryInfo` usually checks its files in scattered `if (!file.Exists)` calls, and the path tricks (`..`, symlinks, hidden folders, a sibling folder that shares a prefix) get missed. This package states those checks as `Constraint<T>` values, one named rule per concern, built on [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities). Each rule reports every offending path with its full path as evidence, and reads the file system the path came from, so a test on a `MockFileSystem` checks the code that runs against the disk.

It depends only on `Nullean.Entities` and `System.IO.Abstractions`, and it is trim and AOT clean (`./build.sh aot` publishes an app using these rules NativeAOT). It targets `net10.0` and `net11.0`. To get counter-examples for these rules with no hand-written file system, add the optional [`Nullean.Entities.CsCheck.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.FileSystem).

## Install

```bash
dotnet add package Nullean.Entities.FileSystem
# only to build a MockFileSystem in the examples below and in your tests
dotnet add package System.IO.Abstractions.TestingHelpers
```

## Get going

Declare the rules for a model that holds files, then validate a value. This example runs against a `MockFileSystem`; pass `new System.IO.Abstractions.FileSystem()` to run it on the disk.

```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Nullean.Entities;
using Nullean.Entities.FileSystem;

var fs = new MockFileSystem();
fs.AddDirectory("/docs");
fs.AddFile("/docs/a.png", new MockFileData("png"));
fs.AddFile("/elsewhere/secret.png", new MockFileData("png"));

var rules = ConstraintSet.From(
	("ImagesExist", Files<Docset>.Exist(d => d.Images)),
	("ImagesStayInRoot", Files<Docset>.StayInside(d => d.Root, d => d.Images))
);

var docset = new Docset(
	fs.DirectoryInfo.New("/docs"),
	[fs.FileInfo.New("/docs/a.png"), fs.FileInfo.New("/docs/missing.png"), fs.FileInfo.New("/docs/../elsewhere/secret.png")]
);

foreach (var v in rules.Validate(docset))
	Console.WriteLine($"{v.ConstraintId} ({v.Member}): {v.Message}");

internal sealed record Docset(IDirectoryInfo Root, IReadOnlyList<IFileInfo> Images);
```

Output:

```text
ImagesExist (Images): '/docs/missing.png' does not exist.
ImagesStayInRoot (Images): '/elsewhere/secret.png' is outside '/docs'.
```

`..` is resolved before the comparison, so the third path is reported as `/elsewhere/secret.png`, and `/docs/a.png` passes both rules.

The same rules as a `[Constraints]` companion, the usual way to declare them. The generator turns each `static readonly Constraint<T>` field into a named rule:

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

## The value: path rules you would not write by hand

| Shape | Violated when |
|---|---|
| `Exist`, `DirectoriesExist` | a file or directory is missing (evidence: its full path) |
| `StayInside(root, paths)` | a path, once `..` is resolved, is not inside the root. A sibling that shares a prefix (`/docs-other` against `/docs`) is outside |
| `NotSymlink(paths, root?)` | a path is a symbolic link, or sits below one (checked up to the root) |
| `NoHiddenSegments(root, paths)` | a directory between the root and the path starts with a dot |
| `HaveExtension(paths, extensions)` | the extension is not one of the list, ignoring case |
| `NotEmpty(files)`, `MaxSize(files, bytes)` | an existing file has no content, or is larger than the limit (a missing file is `Exist`'s to report) |
| `UniqueIgnoringCase(paths)` | two paths differ only in case: two files on Linux, one on macOS and Windows |

Each shape takes `message:` (one text for every finding), `describe:` (a text per path) and `at:` (a `SourceLocation` for the item, so a finding can point at the YAML line), and reports the selected member as `Violation.Member`. Use `.WithSeverity(Severity.Warning)` for a rule that is not an error yet.

## What you get

**Rules (`Files<T>`)**
- `Exist`, `DirectoriesExist`: the file or folder is there.
- `StayInside`: nothing reaches out of the content folder.
- `NotSymlink`: no link, and no file reached through a linked folder.
- `NoHiddenSegments`: nothing comes from `.git` or another dot folder.
- `HaveExtension`, `NotEmpty`, `MaxSize`, `UniqueIgnoringCase`: content and naming checks.

**Items that are not files.** When the model's items are a table-of-contents entry or a redirect target, `Exist` and `DirectoriesExist` take the items and a function that resolves each to the file it names. `describe` and `at` then speak about the item:

```csharp
Files<Docset>.DirectoriesExist(
	d => d.Folders,                                   // the entries
	(d, f) => d.FileSystem.DirectoryInfo.New(Path.Join(d.Root.FullName, f.Path)),
	describe: f => $"Folder '{f.Path}' does not exist.",
	at: f => new SourceLocation(f.File, f.Line, f.Column, f.Path.Length));
```

**Path arithmetic (`FilePaths`).** The helpers the shapes use, public for a rule that needs the answer inside a larger check: `IsInside(root, path, comparison)`, `SymlinkAt`, `HiddenSegment`, and `UnsafeToRead(path, root)`, the reason a path may not be read as content (a link, or a hidden directory, on the way) or null. A chain that stops at the first problem (right extension, inside the root, safe to read, exists) keeps its own wording and asks `FilePaths` instead of re-deriving the rules.

## Pitfalls and limits

- The rules read the file system the path came from (`IFileSystemInfo.FileSystem`). Build the model on a `MockFileSystem` in tests and on `System.IO.Abstractions.FileSystem` in production.
- A path the file system refuses is reported as a violation of that rule, not thrown.
- `StayInside` compares ordinally; a path equal to the root is inside it.
- `UniqueIgnoringCase` reports collisions that are two files on Linux and one file on macOS and Windows.
- `MaxSize` and `NotEmpty` skip a missing file; pair them with `Exist`.
- Symlink behaviour depends on what the `IFileSystem` implementation reports (`MockFileSystem` supports `CreateSymbolicLink`).

## Next

- Generate counter-examples for these rules: [`Nullean.Entities.CsCheck.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.FileSystem)
- The core package: [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities)
- Getting started: https://entities.nullean.net/getting-started/
- Adopting on an existing domain: https://entities.nullean.net/guides/adopting/
- Integration with real services: https://entities.nullean.net/integration/
- A runnable example (docs site build): https://github.com/nullean/entities/tree/main/examples/Apps/DocsSiteBuild
