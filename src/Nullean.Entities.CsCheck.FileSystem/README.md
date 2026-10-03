# Nullean.Entities.CsCheck.FileSystem

AutoGen for models that hold the file system. `IFileSystem`, `IFileInfo` and `IDirectoryInfo` have no constructor and what matters is what is on the disk, so they cannot be built by shape.
`UseFileSystem()` gives AutoGen a `MockFileSystem` **per generated value**:

```csharp
record Docset(IFileSystem FileSystem, IDirectoryInfo Root, IFileInfo SourceFile, IReadOnlyList<IFileInfo> Images);

var gen = GenAuto.Of<Docset>(AutoMode.Mixed, o => o.UseFileSystem());
DocsetConstraints.All.Probe(gen);     // a missing file, a symlink, a path outside the root... without writing a file system
DocsetConstraints.All.Laws(valid, counterExamples);
```

Every file-system member of one value belongs to the same world, so `SourceFile`, `Root` and `FileSystem` agree. `Plausible` data is files that exist inside the root. `Mixed` and `Adversarial`
add what the rules about files exist to catch: a missing file, an empty one, a symbolic link, a file below a linked directory, one below a hidden directory, one outside the root, one reached
through `..`, one in a different case. Worlds are deterministic from the seed, and a shrunk failure prefers the plain existing file.

Not covered: `Boundaries()` swaps edge values into single properties and does not know the file kinds, so counter-examples for `Laws` on these members are written by hand (in the same world:
`d.FileSystem.AddFile(...)`).
