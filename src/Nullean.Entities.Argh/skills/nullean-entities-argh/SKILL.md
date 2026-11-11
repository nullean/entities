---
name: nullean-entities-argh
description: >-
  Use when a Nullean.Argh command-line tool should reject bad flag values using the domain's Nullean.Entities constraints.
  It registers a constraint set as an Argh validator, so a violation prints as `Error: --flag: message` and exits with code 2.
---

# Nullean.Entities.Argh

Validates the option objects an Argh command binds (`[AsParameters]`, global and namespace options) against a `ConstraintSet<T>`, right after Argh's own checks such as `[Range]` and `[Existing]`. For System.CommandLine use `Nullean.Entities.SystemCommandLine` instead.

## The calls you need

The options type declares its rules; give each violation the member it is about:

```csharp
public sealed partial record ServeOptions(string Host, int Port)
{
	[Constraint]
	public static readonly Constraint<ServeOptions> PortInRange = Constraints<ServeOptions>.Check(
		o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Port), "must be between 1 and 65535")]
	);
}

public static void Serve([AsParameters("serve")] ServeOptions options) =>
	Console.WriteLine($"Listening on {options.Host}:{options.Port}");
```

Register once at startup, before `ArghRuntime.RunAsync(args)`:

```csharp
ArghConstraints.Register<ServeOptions>();   // T.Constraints, for a type that declares its own rules
ArghConstraints.Register(ServeRules.All);   // or a companion's ConstraintSet<T>
```

## Gotchas

- `Register` is static and process-wide: it applies to every command that binds that type, and to `TryParseArgh` in tests. Call it before the parser runs (startup code or a `[ModuleInitializer]`), not inside a handler.
- `T` must be a class (records are fine). `Register<T>()` without arguments also needs `T : IHaveConstraints<T>`, which the generator adds to a `partial` type with `[Constraint]` fields.
- Only `Severity.Error` stops the command (exit code 2, handler not run). Warnings and hints are written to stderr as `Warning: message` and the handler still runs.
- The flag in the message comes from `Violation.Member`. Use `Violation.Error(nameof(Port), "...")`, not `Violation.Error("...")`, or the error names no flag.
- Constraints are synchronous and pure. Use Argh's `[Existing]` and similar attributes for file system checks.
- Built against Nullean.Argh 0.21 (`ArghValidation`).

Docs: https://entities.nullean.net
