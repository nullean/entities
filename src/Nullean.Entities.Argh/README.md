# Nullean.Entities.Argh

Validate the option objects a [Nullean.Argh](https://github.com/nullean/argh) command receives against `Nullean.Entities` constraints. A violation is reported like any other bad value:

```
Error: --serve-port: must be between 1 and 65535

<the flag's help rows>

Run 'app serve --help' for usage.
```

and the process exits with code 2. Requires Nullean.Argh 0.21.

```csharp
ArghConstraints.Register<ServeOptions>();           // ServeOptions declares [Constraints]; or Register(ServeRules.All)

app.Map("serve", ([AsParameters("serve")] ServeOptions options) => ...);
```

The registration applies to every command that binds that type, global and namespace options included, and to `TryParseArgh` in tests. Only errors stop the command; warnings and hints are printed to stderr.
