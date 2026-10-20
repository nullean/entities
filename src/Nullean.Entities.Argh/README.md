# Nullean.Entities.Argh

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Argh.svg)](https://www.nuget.org/packages/Nullean.Entities.Argh)

**A bad flag value stops your command with a clear `Error: --flag: message` and exit code 2, using the same rules your domain already has.**

Without this package, a command-line tool either checks its option object by hand at the top of every handler or lets a bad value reach the code that uses it. This package validates the option objects a [Nullean.Argh](https://github.com/nullean/argh) command receives against `Nullean.Entities` constraints, and reports a violation like any other bad value Argh finds.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and `Nullean.Argh.Interfaces`; it does not need the other integration packages. It is optional, it uses no reflection, and it is trim and NativeAOT clean. Requires Nullean.Argh 0.21.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Argh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Argh
```

## Get going

Register the constraints for the option type once at startup, before `RunAsync`. The registration lives in a module initializer here because Argh's generated parser runs the validator for every command that binds the type; this is the shape the package's own tests use.

```csharp
using System.Runtime.CompilerServices;
using Nullean.Argh;
using Nullean.Argh.Builder;
using Nullean.Entities;
using Nullean.Entities.Argh;

public sealed partial record ServeOptions(string Host, int Port)
{
	[Constraint]
	public static readonly Constraint<ServeOptions> PortInRange = Constraints<ServeOptions>.Check(
		o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Port), "must be between 1 and 65535")]
	);
}

internal static class Handlers
{
	public static void Serve([AsParameters("serve")] ServeOptions options) =>
		Console.WriteLine($"Listening on {options.Host}:{options.Port}");
}

internal static class Registration
{
	[ModuleInitializer]
	internal static void Register()
	{
		ArghConstraints.Register<ServeOptions>();   // ServeOptions declares its rules; or Register(ServeRules.All)
		var app = new ArghBuilder();
		app.Map("serve", Handlers.Serve);
	}
}
```

Run it through Argh's runtime (`ArghRuntime.RunAsync(args)` in `Nullean.Argh.Runtime`) or your generated entry point. A bad value:

```text
$ app serve --serve-host localhost --serve-port 70000
Error: --serve-port: must be between 1 and 65535

<the flag's help rows>

Run 'app serve --help' for usage.
```

The process exits with code 2 and the handler does not run. With `--serve-port 8080` the handler runs and the exit code is 0.

The value: the rule is written once, on the type, and the same set also guards your service layer and your generated tests. Change `PortInRange` and the CLI, the HTTP API and the property tests change together.

## What you get

- `ArghConstraints.Register<T>()` where `T : class, IHaveConstraints<T>`: registers `T.Constraints` (a type with `[Constraint]` fields or `[Constraints]`).
- `ArghConstraints.Register<T>(constraintSet)`: registers a companion's set, for example `ServeRules.All`.

The registration applies to every command that binds that type: `[AsParameters]` objects, global options and namespace options, and `TryParseArgh` in tests. Constraints run right after Argh's own checks (`[Range]`, `[Existing]` and so on). The error names the flag from `Violation.Member`.

## Pitfalls and limits

- Call `Register` at startup, before `RunAsync`. The generated parser looks the validator up when it parses.
- Only errors stop the command. Warnings and hints are written to stderr as `Warning: message` or `Hint: message` and do not fail it.
- The flag in the message comes from `Violation.Member`; give each violation a member (`Violation.Error(nameof(Port), "...")`).
- Constraints are synchronous and pure: do not check that a file exists or a host answers here. Use Argh's `[Existing]` and similar checks for the file system.
- Needs Nullean.Argh 0.21 (`ArghValidation`); other versions are untested.

## Next

- Integration recipes: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Argh: https://github.com/nullean/argh
- Web APIs: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Options validation and registration: https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.Argh.Tests
