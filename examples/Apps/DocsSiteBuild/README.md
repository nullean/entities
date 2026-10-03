# DocsSiteBuild

A tiny documentation build that reads a docset from a file system and refuses to build it when its files are wrong. `dotnet run --project examples/Apps/DocsSiteBuild` builds a docset in a temporary directory, then breaks the directory in the ways the rules exist for (it removes the directory afterwards; it exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Docset.cs` | A model that holds **live file handles** (`IFileSystem`, `IDirectoryInfo`, `IFileInfo`) and the `Files<T>` rules over them: exists, stays inside the root, not a symlink, not under a hidden folder, right extension. |
| `SiteBuilder.cs` | Load, validate, and build only if there is no error. Each finding points at the line of `docset.yml` that named the file, from the path in the violation's evidence. |
| `Program.cs` | The scenarios, on the real disk (including a real symbolic link). |

Tests: `tests/Nullean.Entities.Apps.Tests/DocsSiteBuild` run the same builder and rules on a `MockFileSystem`, where a symlink or a hidden folder is one line. Then `GenAuto.Of<Docset>(mode, o => o.UseFileSystem())` (`Nullean.Entities.CsCheck.FileSystem`) generates whole file-system worlds (a missing file, a link, a path that escapes, a hidden folder, a wrong extension...) and `Probe` shows that every rule can be broken by some generated world, with no file system written by hand.

Things worth noticing:

- The rules read `IFileInfo.FileSystem`, the file system the handle came from, so a test that builds the model on a `MockFileSystem` checks the very code that runs on the disk. There is no second implementation of "does this file exist" for tests.
- `StayInside` resolves `..` first, and a sibling that shares a prefix (`/docs-other` for `/docs`) is outside. Both are the checks that a `StartsWith(root)` gets wrong.
- A path the file system refuses is a violation of the rule, not an exception, so a build reports everything wrong in one pass.
- Generated files are `.md`, so a rule that expects `.png` of an image does not hold for *plausible* generated data (`Probe` would call it a contradiction). Use a baseline you build by hand for such rules, or generate with `Mixed`/`Adversarial` and read the rule as "the generator reaches a violation".
