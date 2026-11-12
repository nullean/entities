---
navigation_title: Command-line apps
description: Validate the options of a Nullean.Argh command against your constraints, and fail like any other bad flag.
---

# Command-line apps (Nullean.Argh)

```bash
dotnet add package Nullean.Entities.Argh
```

For a [Nullean.Argh](https://github.com/nullean/argh) command, register the constraints for its option type, and a violation fails the way Argh's own checks do:

```csharp
ArghConstraints.Register<ServeOptions>();             // before RunAsync; or Register(ServeRules.All) for a companion's set

app.Map("serve", ([AsParameters("serve")] ServeOptions options) => ...);
// $ app serve --serve-port 70000
// Error: --serve-port: must be between 1 and 65535        (then the flag's help rows and the usage line; exit code 2, handler not run)
```

It works through Argh's `ArghValidation` hook, so it covers `[AsParameters]` objects, global and namespace options, and `TryParseArgh` in tests. Constraints run right after Argh's own
checks (`[Range]`, `[Existing]` and so on), and the error names the flag from `Violation.Member`.

The package builds on `Nullean.Entities` and `Nullean.Argh.Interfaces`, needs Nullean.Argh 0.28, uses no reflection, and is trim and NativeAOT clean. The
[package README](https://www.nuget.org/packages/Nullean.Entities.Argh) has a complete program, with the registration in a module initializer.
