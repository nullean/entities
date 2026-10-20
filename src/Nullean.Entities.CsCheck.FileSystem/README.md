# Nullean.Entities.CsCheck.FileSystem

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.FileSystem.svg)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.FileSystem)

**Test your file rules against missing, symlinked, hidden and out-of-root files without writing a file system by hand.**

`IFileSystem`, `IFileInfo` and `IDirectoryInfo` have no constructor, and what matters is what is on the disk, so a generator cannot build them by shape. This package teaches the `GenAuto` generator of [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck) to build them: `UseFileSystem()` gives each generated value its own `MockFileSystem`. It is the test-side partner of [`Nullean.Entities.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.FileSystem), and it is optional. It uses reflection through AutoGen, so it is for test projects and is not trim or AOT clean. It targets `net10.0` and `net11.0`.

## Install

```bash
dotnet add package Nullean.Entities.CsCheck.FileSystem
```

It brings `Nullean.Entities.CsCheck`, `Nullean.Entities.FileSystem` and `System.IO.Abstractions.TestingHelpers`. Put it in a test project.

## Get going

Declare the file rules once, generate worlds, and ask which rules a generated world can break:

```csharp
using System.IO.Abstractions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;
using Nullean.Entities.FileSystem;

internal sealed record Docset(IFileSystem FileSystem, IDirectoryInfo Root, IFileInfo SourceFile, IReadOnlyList<IFileInfo> Images);

public class DocsetTests
{
	private static readonly ConstraintSet<Docset> Rules = ConstraintSet.From(
		("RootExists", Files<Docset>.DirectoriesExist(d => [d.Root])),
		("SourceFileExists", Files<Docset>.Exist(d => [d.SourceFile])),
		("ImagesExist", Files<Docset>.Exist(d => d.Images)),
		("ImagesStayInRoot", Files<Docset>.StayInside(d => d.Root, d => d.Images)),
		("ImagesAreNotSymlinks", Files<Docset>.NotSymlink(d => d.Images, d => d.Root)),
		("NoHiddenFolders", Files<Docset>.NoHiddenSegments(d => d.Root, d => d.Images))
	);

	[Test]
	public void Every_file_rule_can_be_broken_by_a_generated_world()
	{
		var gen = GenAuto.Of<Docset>(AutoMode.Mixed, o => o.UseFileSystem());

		var report = Rules.Probe(gen, samples: 1500, throwOnContradiction: false);

		if (report.NeverViolated.Count > 0)
			throw new Exception("These rules can never fire: " + string.Join(", ", report.NeverViolated));
	}
}
```

The test uses [TUnit](https://tunit.dev) attributes, the framework this repository's tests use. A rule that no generated world can break is reported by name: it may be a tautology, or its counter-example needs a shape the generator does not draw. `Probe` also prints a held and violated count per rule with `report.ToString()`.

## The value: the cases you forgot

Every file-system member of one value belongs to the same world, so `SourceFile`, `Root` and `FileSystem` agree. The `AutoMode` you pass decides what the world holds:

| Mode | Files in the world |
|---|---|
| `Plausible` | files that exist inside the root |
| `Mixed`, `Adversarial` | also a missing file, an empty one, a symbolic link, a file below a linked directory, one below a hidden directory, one outside the root, one reached through `..`, one in a different case, one with the wrong extension |

The wrong extension is `.bin`; generated files are otherwise `.md`, so a rule that expects `.png` of an image will not hold for plausible data. Worlds are deterministic from the seed, and a shrunk failure prefers the plain existing file.

Find a counter-example for each rule with none written by hand, using `AutoTest` from `Nullean.Entities.CsCheck` and the same `UseFileSystem()` option:

```csharp
var report = AutoTest.For(Rules, Gen.Const(valid)).Options(o => o.UseFileSystem()).CounterExampleReport();
```

where `valid` is one `Docset` whose files all satisfy the rules (for example, a draw from `AutoMode.Plausible` with at least one image). `Boundaries()` and `Laws()` know the file kinds too: pass `options: o => o.UseFileSystem()` and each kind (a missing file, a symlink, a hidden directory) is swapped, in the baseline's own file system, into the file members and into one-element lists of them. A rule that two others always trip with it, like a missing root and "images stay inside the root", is reported as an overlap. `Laws()` is marked obsolete in favour of `AutoTest`.

## What you get

- `o.UseFileSystem()` (an extension on `AutoOptions`, namespace `Nullean.Entities.CsCheck.FileSystem`): builds `IFileSystem`, `IFileInfo`, `IDirectoryInfo` and `IFileSystemInfo` members from one `MockFileSystem` per generated value. Pass it to `GenAuto.Of<T>(mode, configure)`, to `AutoTest...Options(...)`, or to any API that takes `AutoOptions`.
- The generated values work with everything in `Nullean.Entities.CsCheck`: `Probe`, `ForAll`, `AutoTest`, `Spec()`.

## Pitfalls and limits

- Every generated value allocates its own `MockFileSystem`; it is slower than a plain record. Lower `samples` if a probe is slow.
- The root is `/docs` (`C:\docs` on Windows) and the outside folder is `/elsewhere` (`C:\elsewhere`). A rule that depends on other paths needs a hand-built baseline.
- `MockFileSystem` is in-memory, so rules about permissions, locks or real disk behaviour are not exercised.
- Rules whose counter-examples need a structure the generator cannot draw (for example a specific folder name your rule looks for) still need one written by hand.
- It reaches the file-system members through AutoGen's reflection; do not reference it from an AOT-published app.

## Next

- The rules being tested: [`Nullean.Entities.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.FileSystem)
- The generator and `AutoTest`: [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck)
- Counter-examples and what is left to write: https://entities.nullean.net/guides/counter-examples/
- Testing API reference: https://entities.nullean.net/reference/testing-api/
- Generated data reference: https://entities.nullean.net/reference/generated-data/
- Integration with real services: https://entities.nullean.net/integration/
- A worked example: https://github.com/nullean/entities/tree/main/examples/Apps/DocsSiteBuild
