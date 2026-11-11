---
name: nullean-entities-systemcommandline
description: >-
  Use when a System.CommandLine 2.0 command should reject option values that break Nullean.Entities constraints before its action runs.
  It adds `Command.ValidateConstraints`, which reports a violation like a parse error (`--port: message`, then help, exit code 1).
---

# Nullean.Entities.SystemCommandLine

Wraps a command's action so the options object it binds is checked against a `ConstraintSet<T>` first, after defaults are applied. For Nullean.Argh tools use `Nullean.Entities.Argh` instead.

## The calls you need

Here `ServeOptions` is a `partial record ServeOptions(int Port, int Attempts)` with `[Constraint]` fields. Write the binding once, use it in the action, and hand it to `ValidateConstraints` after `SetAction`:

```csharp
var port = new Option<int>("--port", "-p") { DefaultValueFactory = _ => 8080 };
var retries = new Option<int>("--max-retries") { DefaultValueFactory = _ => 3 };
var root = new RootCommand("Serves the site") { port, retries };

ServeOptions Bind(ParseResult r) => new(r.GetValue(port), r.GetValue(retries));

root.SetAction(result => Serve(Bind(result)));
root.ValidateConstraints(Bind);                    // ServeOptions declares its rules
// root.ValidateConstraints(Bind, ServeRules.All); // or a companion's set

return await root.Parse(args).InvokeAsync();
```

When a member does not kebab-case to an option name (`Attempts` is not `--max-retries`), map it:

```csharp
root.ValidateConstraints(Bind, new Dictionary<string, Symbol> { [nameof(ServeOptions.Attempts)] = retries });
```

## Gotchas

- Call it after `SetAction`: it throws `InvalidOperationException` when the command has no action, and a later `SetAction` silently drops the check.
- It wraps the action instead of adding to `Command.Validators`, so `Parse(args).Errors` never contains constraint errors. Test through `InvokeAsync` with an `InvocationConfiguration` whose `Output` and `Error` are `StringWriter`s.
- Exit code 1 and the action does not run, the same as a System.CommandLine parse error. Its own errors (bad conversion, missing required option) come first.
- The option is found from `Violation.Member` in kebab-case (`MaxRetries` to `--max-retries`), against names and aliases, then arguments (printed `<name>`), then recursive parent options. Unmatched members print as `Member: message`.
- Only errors stop the command; warnings and hints go to the error writer as `Warning: message` and the action runs.
- Each command is guarded separately: call it on the subcommand whose action binds the options.

Docs: https://entities.nullean.net
