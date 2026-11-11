---
name: nullean-entities-cscheck-filesystem
description: >-
  Use when property-testing a model that holds IFileSystem, IFileInfo or IDirectoryInfo members, or rules written with
  Nullean.Entities.FileSystem's Files<T>. UseFileSystem() teaches GenAuto to build those members from one MockFileSystem per generated value.
---

# Nullean.Entities.CsCheck.FileSystem

One extension, `AutoOptions.UseFileSystem()` (namespace `Nullean.Entities.CsCheck.FileSystem`), that works anywhere `AutoOptions` is taken.
The rules themselves come from `Nullean.Entities.FileSystem`; generation, `AutoTest` and `Probe` come from `Nullean.Entities.CsCheck`.

## The calls you need

```csharp
// file rules over a model; every file member of one value shares one MockFileSystem
static readonly ConstraintSet<Docset> Rules = ConstraintSet.From(
	("SourceFileExists", Files<Docset>.Exist(d => [d.SourceFile])),
	("ImagesStayInRoot", Files<Docset>.StayInside(d => d.Root, d => d.Images)),
	("ImagesAreNotSymlinks", Files<Docset>.NotSymlink(d => d.Images, d => d.Root)));
```

```csharp
// can a generated world break every rule? Mixed adds missing, symlinked, hidden and out-of-root files
var gen = GenAuto.Of<Docset>(AutoMode.Mixed, o => o.UseFileSystem());
var report = Rules.Probe(gen, samples: 1500, throwOnContradiction: false);
if (report.NeverViolated.Count > 0) throw new Exception(string.Join(", ", report.NeverViolated));
```

```csharp
// a valid baseline (Plausible worlds hold only existing files inside the root), then counter-examples and edges in its own world
var valid = GenAuto.Of<Docset>(AutoMode.Plausible, o => o.UseFileSystem()).Where(d => d.Images.Count > 0).Generate(new PCG(5u, 1UL), null, out _);
var missing = AutoTest.For(Rules, Gen.Const(valid)).Options(o => o.UseFileSystem()).CounterExampleReport().Missing;
var edges = Rules.Boundaries(valid, options: o => o.UseFileSystem());
```

## Gotchas

- Without `UseFileSystem()` generating such a model throws `NotSupportedException` whose message names this call.
- Pass `options: o => o.UseFileSystem()` to `Boundaries` and give it a baseline: the default baseline is generated without your options.
- `Plausible` worlds contain only files that exist under the root, so they never break a file rule. Probe with `Mixed` or `Adversarial`.
- Generated files are `.md`; the wrong-extension case is `.bin`. A rule that expects `.png` fails on plausible data.
- The root is `/docs` and the outside folder `/elsewhere` (`C:\docs`, `C:\elsewhere` on Windows). Rules that need other paths need a hand-built baseline.
- Two generated values never share a world, and each allocates a `MockFileSystem`: lower `samples` when a probe is slow.
- In-memory only: permissions, locks and real disk behaviour are not exercised. Test projects only; not AOT clean.

Docs: https://entities.nullean.net
