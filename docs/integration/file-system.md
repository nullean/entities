---
navigation_title: File systems
description: Named rules for models that hold IFileInfo and IDirectoryInfo, and generated file systems that break them.
---

# File systems (System.IO.Abstractions)

```bash
dotnet add package Nullean.Entities.FileSystem            # the rules
dotnet add package Nullean.Entities.CsCheck.FileSystem    # tests only: generated file systems
```

If the model holds `IFileInfo`, `IDirectoryInfo` or `IFileSystem`, `Nullean.Entities.FileSystem` has the file rules: `Files<T>.Exist`, `DirectoriesExist`, `StayInside`, `NotSymlink`,
`NoHiddenSegments` and `HaveExtension`. Each reads the file system the path came from, so a test on a `MockFileSystem` checks the code that runs against the disk, and each reports every
offending path as evidence:

```csharp
var rules = ConstraintSet.From(
	("ImagesExist", Files<Docset>.Exist(d => d.Images)),
	("ImagesStayInRoot", Files<Docset>.StayInside(d => d.Root, d => d.Images))
);
```

```text
ImagesExist (Images): '/docs/missing.png' does not exist.
ImagesStayInRoot (Images): '/elsewhere/secret.png' is outside '/docs'.
```

`..` is resolved before the comparison, so `/docs/../elsewhere/secret.png` is reported as `/elsewhere/secret.png`. The package depends only on `Nullean.Entities` and
`System.IO.Abstractions`, and is trim and AOT clean.

## Generated file systems

`IFileSystem` has no constructor and what matters is what is on the disk, so `GenAuto` cannot build one by shape. `Nullean.Entities.CsCheck.FileSystem` teaches it: `o.UseFileSystem()`
builds one `MockFileSystem` world per value, with missing, symlinked, hidden and outside-the-root files, so `Probe` finds counter-examples without a hand-written file system:

```csharp
var gen = GenAuto.Of<Docset>(AutoMode.Mixed, o => o.UseFileSystem());

var report = Rules.Probe(gen, samples: 1500, throwOnContradiction: false);   // report.NeverViolated names a rule no world could break
```

It uses reflection through `GenAuto`, so it is for test projects. The package READMEs on nuget.org,
[`Nullean.Entities.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.FileSystem) and
[`Nullean.Entities.CsCheck.FileSystem`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.FileSystem), have complete programs, and
[`examples/Apps`](https://github.com/nullean/entities/tree/main/examples/Apps) has a docs build over a file system.
