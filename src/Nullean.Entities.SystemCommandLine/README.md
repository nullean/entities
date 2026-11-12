# Nullean.Entities.SystemCommandLine

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

**A bad option value stops your System.CommandLine command with `--port: must be between 1 and 65535` and exit code 1, like any parse error, using the rules your domain already has.**

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.SystemCommandLine?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.SystemCommandLine)

System.CommandLine 2.0 parses and converts each option, but it has no options-object binding: the action reads the values and builds its own object. Without this package that object is checked by hand at the top of every action, or not at all. This package checks it against `Nullean.Entities` constraints right before the action runs, and reports a violation the way System.CommandLine reports its own errors.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and [System.CommandLine](https://www.nuget.org/packages/System.CommandLine) 2.0; it does not need the other integration packages. It is optional, it uses no reflection, and it is trim and NativeAOT clean.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package System.CommandLine
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.SystemCommandLine
```

## Quick start

Write the binding once, as a function from the parse result to your options type. Use it in the action, and give it to `ValidateConstraints` after `SetAction`:

```csharp
using System.CommandLine;
using Nullean.Entities;
using Nullean.Entities.SystemCommandLine;

var host = new Option<string>("--host") { DefaultValueFactory = _ => "localhost" };
var port = new Option<int>("--port", "-p") { DefaultValueFactory = _ => 8080 };
var retries = new Option<int>("--max-retries") { DefaultValueFactory = _ => 3 };
var root = new RootCommand("Serves the site") { host, port, retries };

ServeOptions Bind(ParseResult r) => new(r.GetValue(host)!, r.GetValue(port), r.GetValue(retries));

root.SetAction(result => Serve(Bind(result)));   // runs only when the constraints hold
root.ValidateConstraints(Bind);                  // after SetAction; ServeOptions declares its rules, or pass a set: ValidateConstraints(Bind, ServeRules.All)

return await root.Parse(args).InvokeAsync();

static void Serve(ServeOptions options) => Console.WriteLine($"Listening on {options.Host}:{options.Port}");

public sealed partial record ServeOptions(string Host, int Port, int MaxRetries)
{
	[Constraint]
	public static readonly Constraint<ServeOptions> PortInRange = Constraints<ServeOptions>.Check(
		o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Port), "must be between 1 and 65535")]
	);

	[Constraint]
	public static readonly Constraint<ServeOptions> RetriesAreBounded = Constraints<ServeOptions>.Check(
		o => o.MaxRetries <= 10 ? [] : [Violation.Error(nameof(MaxRetries), "must be 10 or fewer")]
	);
}
```

A bad value:

```text
$ app --port 70000
--port: must be between 1 and 65535

Description:
  Serves the site

Usage:
  app [options]
...
```

The error goes to stderr (red on a terminal), the help to stdout, the process exits with code 1 and the action does not run: the same output and exit code System.CommandLine gives for `--port abc`. With `--port 8081`, or no `--port` at all (the default `8080` is applied before the check), the action runs.

The value: the rule is written once, on the type, and the same set also guards your service layer and your generated tests. Change `PortInRange` and the CLI, the HTTP API and the property tests change together.

## What you get

- `command.ValidateConstraints<T>(bind, members?)` where `T : IHaveConstraints<T>`: checks `T.Constraints` (a type with `[Constraint]` fields or `[Constraints]`).
- `command.ValidateConstraints<T>(bind, constraintSet, members?)`: checks a companion's set, for example `ServeRules.All`.
- `members`: an optional `IReadOnlyDictionary<string, Symbol>` from a `Violation.Member` to the option or argument it names, for when the names differ.

Which option an error names comes from `Violation.Member`: the member in kebab-case (`Port` to `--port`, `MaxRetries` to `--max-retries`) against the command's option names and aliases, then its arguments (printed as `<name>`), then recursive options of parent commands. A member that matches nothing is printed as is (`Upstream: message`); a rule with no member prints only its message. Every error is printed, one per line.

## Pitfalls and limits

- Call `ValidateConstraints` after `SetAction`. It wraps the command's current action and throws `InvalidOperationException` if there is none; a later `SetAction` replaces the wrapper and the check with it.
- It wraps the action rather than adding to `Command.Validators` on purpose: System.CommandLine 2.0 runs command validators before it fills in option defaults, so a bound `--port` would read `0` instead of its default. The consequence is that the errors are not in `ParseResult.Errors`; `Parse` alone does not report them. Test through `InvokeAsync` with an `InvocationConfiguration` whose `Output` and `Error` are `StringWriter`s.
- System.CommandLine's own errors come first. A value that does not convert or a missing required option stops the run before the constraints are checked.
- Only errors stop the command. Warnings and hints are written to the invocation's error writer as `Warning: message` or `Hint: message` and the action runs.
- Each command is guarded separately: call `ValidateConstraints` on the subcommand whose action binds the options.
- Constraints are synchronous and pure: do not check that a file exists or a host answers here. Use System.CommandLine's `AcceptExistingOnly()` and similar argument validators for that.
- Tested with System.CommandLine 3.0.0.

## Learn more

- This integration on the docs site: https://entities.nullean.net/integration/system-commandline/, and every integration: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- System.CommandLine: https://learn.microsoft.com/dotnet/standard/commandline/
- Nullean.Argh command lines: https://www.nuget.org/packages/Nullean.Entities.Argh
- Web APIs: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.SystemCommandLine.Tests
- Source and issues: https://github.com/nullean/entities
