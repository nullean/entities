---
navigation_title: Options and DI
description: Register constraint sets in Microsoft.Extensions.DependencyInjection and fail startup on configuration that breaks a rule.
---

# Options and dependency injection

```bash
dotnet add package Nullean.Entities.Extensions.DependencyInjection
```

`Nullean.Entities.Extensions.DependencyInjection` registers your `ConstraintSet<T>` values in the service collection, adds an `IConstraintRegistry` that validates an object it only holds
as `object`, and plugs constraints into `IOptions<T>` validation. It depends on `Nullean.Entities` only, uses no reflection, and is trim and NativeAOT clean. The
[ASP.NET Core](aspnetcore.md) and [Argh](argh.md) packages turn these registrations into HTTP and command-line behaviour.

## Fail startup on bad configuration

```csharp
services.AddOptions<ServerOptions>().BindConfiguration("Server").ValidateConstraints(ServerRules.All).ValidateOnStart();
```

A violation fails startup with `OptionsValidationException`, with failures worded `Member: message`:

```text
Microsoft.Extensions.Options.OptionsValidationException: Port: must be between 1 and 65535
```

`ValidateConstraints()` with no argument takes `T.Constraints` for a type that declares its own. Only errors fail the options; warnings and hints do not. A named options builder validates only its own name.

## Register sets

- `services.AddConstraints<T>()` registers `T.Constraints`, for a `T : IHaveConstraints<T>` (a `partial` type with `[Constraint]` fields).
- `services.AddConstraints(OrderRules.All)` registers a companion's set, for types you cannot make `partial`.
- `services.AddConstraints<T, TFor>(companion)` registers a generated companion and makes it resolvable as `IConstraintsFor<T>`.

Every call lands in one `ConstraintRegistry` singleton. It uses the container's `TimeProvider` when one is registered, so time-based rules follow your clock.

## Validate what you hold as `object`

```csharp
var registry = provider.GetRequiredService<IConstraintRegistry>();

if (registry.TryValidate(body, out var violations) && violations.Count > 0)
{
	var errors = ViolationErrors.ToDictionary(violations);   // { "Email": ["must be an email address"] }
}
```

`TryValidate` returning `false` means nothing is registered for that type, not that the value is valid. It looks up the exact runtime type, so a derived type is not validated by its
base's set. `ViolationErrors.ToDictionary` groups errors into ProblemDetails' `errors` shape and drops warnings and hints. A second `AddConstraints` for the same `T` replaces the first.
