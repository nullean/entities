---
navigation_title: System.CommandLine
description: Check the options a System.CommandLine command binds against your constraints before its action runs, and fail like a parse error.
---

# Command-line apps (System.CommandLine)

```bash
dotnet add package Nullean.Entities.SystemCommandLine
```

System.CommandLine 2.0 does not bind options to an object for you: you read each value in the action (`result.GetValue(port)`). Write that as one `Bind` function, use it in
the action, and hand it to `ValidateConstraints` after `SetAction`. The constraints are checked right before the action runs:

```csharp
var host = new Option<string>("--host") { DefaultValueFactory = _ => "localhost" };
var port = new Option<int>("--port", "-p") { DefaultValueFactory = _ => 8080 };
var retries = new Option<int>("--max-retries") { DefaultValueFactory = _ => 3 };
var root = new RootCommand("Serves the site") { host, port, retries };

ServeOptions Bind(ParseResult r) => new(r.GetValue(host)!, r.GetValue(port), r.GetValue(retries));

root.SetAction(result => Serve(Bind(result)));   // runs only when the constraints hold
root.ValidateConstraints(Bind);                  // after SetAction; or ValidateConstraints(Bind, ServeRules.All) for a companion's set
// $ app --port 70000
// --port: must be between 1 and 65535           (on stderr, then the help on stdout; exit code 1, action not run)
```

The output is what System.CommandLine prints for its own parse errors (the error lines in red on a terminal, a blank line, then the help), and the exit code is the same 1.
System.CommandLine's own errors (a value that does not convert, a missing required option) still come first: the constraints only run on a parse that succeeded.
Warnings and hints are written to the invocation's error writer as `Warning: message` and do not stop the command.

## Which option a violation names

The option comes from `Violation.Member`, by name: the member in kebab-case (`Port` is `--port`, `MaxRetries` is `--max-retries`) against each option's name and aliases,
then the command's arguments by name (`<upstream>`), then recursive options of the parent commands. When the names differ, pass the mapping:

```csharp
root.ValidateConstraints(r => new ProxyOptions(r.GetValue(to)!), ProxyRules.All,
	new Dictionary<string, Symbol> { [nameof(ProxyOptions.Upstream)] = to });
// --to: must be an https:// URL
```

A member that matches nothing is printed as is (`Upstream: must be an https:// URL`), and a rule without a member prints only its message.

## Why it wraps the action

System.CommandLine has `Command.Validators`, but in 2.0 a command validator runs before the parser fills in option defaults: a validator that binds the options sees `0` for an
omitted `--port` whose default is `8080`, and an error it adds also hides a missing required option. So `ValidateConstraints` wraps the command's action instead, where the
parse result is complete. That is why it must be called after `SetAction` (it throws if the command has no action yet), and why the errors are not in `ParseResult.Errors`:
a test calls `InvokeAsync` with an `InvocationConfiguration` whose `Output` and `Error` are `StringWriter`s and reads the text and the exit code.

The package builds on `Nullean.Entities` and `System.CommandLine` 3.0 (tested with 3.0.0), uses no reflection, and is trim and NativeAOT clean; the AOT smoke app in this
repository publishes it. Every snippet on this page is backed by `tests/Nullean.Entities.SystemCommandLine.Tests/CommandConstraintsTests.cs`, and the
[package README](https://www.nuget.org/packages/Nullean.Entities.SystemCommandLine) has a complete program.
