---
name: nullean-entities-extensions-dependencyinjection
description: >-
  Use when registering Nullean.Entities constraint sets in an IServiceCollection, failing startup on options that break their rules,
  or validating an object held only as `object`. It adds AddConstraints, OptionsBuilder.ValidateConstraints and a type-erased IConstraintRegistry.
---

# Nullean.Entities.Extensions.DependencyInjection

Registers constraint sets into one `ConstraintRegistry` singleton and validates `IOptions<T>` against them, without reflection. It is the base the ASP.NET Core integration builds on; for HTTP 400 responses add `Nullean.Entities.AspNetCore` on top.

## The calls you need

Register sets:

```csharp
services.AddConstraints<SignUp>();          // T.Constraints, for a partial type with [Constraint] fields
services.AddConstraints(OrderRules.All);    // a companion's ConstraintSet<T>
services.AddConstraints<Order, OrderRules.TypeConstraints>(OrderRules.Instance);   // also resolvable as IConstraintsFor<Order>
```

Fail fast on bad configuration (add `.BindConfiguration("Server")` from Microsoft.Extensions.Options.ConfigurationExtensions to read appsettings):

```csharp
services.AddOptions<ServerOptions>()
	.ValidateConstraints(ServerRules.All)   // or .ValidateConstraints() when ServerOptions declares its own rules
	.ValidateOnStart();
```

Validate something you only hold as `object`, and shape the errors like ProblemDetails:

```csharp
var registry = provider.GetRequiredService<IConstraintRegistry>();
if (registry.TryValidate(body, out var violations) && violations.Count > 0)
{
	Dictionary<string, string[]> errors = ViolationErrors.ToDictionary(violations);   // { "Email": ["must be an email address"] }
}
```

## Gotchas

- `TryValidate` returning `false` means no set is registered for that type, not that the value is valid. Check `violations.Count` for validity.
- Lookup is by exact runtime type (`instance.GetType()`): a derived type is not checked by its base's set.
- The registry is built the first time it is resolved. A later `AddConstraints` for the same `T` replaces the earlier one.
- It uses the container's `TimeProvider` when one is registered, so time-based rules follow a test clock.
- Options failures read `Member: message` and include errors only; warnings never block startup. A named options builder validates only its own name.
- `ValidateConstraints()` without a set needs `T : class, IHaveConstraints<T>`; an options class you cannot make `partial` needs a companion set.
- Each set is also registered as a singleton `ConstraintSet<T>` you can inject.

Docs: https://entities.nullean.net
