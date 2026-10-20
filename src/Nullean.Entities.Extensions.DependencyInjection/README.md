# Nullean.Entities.Extensions.DependencyInjection

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection)

**Declare a constraint set once, and your app refuses to start with bad configuration instead of failing on the first request.**

Without this package, every framework hook that needs a rule (an options validator, an endpoint filter, a command-line parser) either reflects over types or gets the rule wired by hand. This package registers your `ConstraintSet<T>` values in `Microsoft.Extensions.DependencyInjection`, adds a type-erased `IConstraintRegistry` that validates an object it only holds as `object`, and plugs constraints into `IOptions<T>` validation.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and nothing else (no CsCheck, no Rx). It is optional, it uses no reflection, and it is trim and NativeAOT clean. [Nullean.Entities.AspNetCore](https://www.nuget.org/packages/Nullean.Entities.AspNetCore) and [Nullean.Entities.Argh](https://www.nuget.org/packages/Nullean.Entities.Argh) are the packages that turn these registrations into HTTP and command-line behaviour.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Extensions.DependencyInjection
```

Install this package when you want to validate `IOptions<T>` or register constraints in a service collection. Install `Nullean.Entities.AspNetCore` for web endpoints and `Nullean.Entities.Argh` for CLI options; the ASP.NET Core package already depends on this one.

## Get going

Fail startup when configuration breaks a rule. This is a complete top-level program.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nullean.Entities;
using Nullean.Entities.Extensions.DependencyInjection;

var services = new ServiceCollection();
services
	.AddOptions<ServerOptions>()
	.Configure(o => o.Port = 0)
	.ValidateConstraints(ServerRules.All)
	.ValidateOnStart();

using var provider = services.BuildServiceProvider();
var port = provider.GetRequiredService<IOptions<ServerOptions>>().Value.Port;

public sealed class ServerOptions
{
	public int Port { get; set; }
}

internal static class ServerRules
{
	public static readonly ConstraintSet<ServerOptions> All = ConstraintSet.From(
		("PortInRange", Constraints<ServerOptions>.Check(
			o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(ServerOptions.Port), "must be between 1 and 65535")]
		))
	);
}
```

Reading `.Value` throws:

```text
Microsoft.Extensions.Options.OptionsValidationException: Port: must be between 1 and 65535
```

`ValidateOnStart()` runs the check when a generic host starts (`Microsoft.Extensions.Hosting`), so the host throws the same exception before the first request. Failures are worded `Member: message`; a violation with no member is the bare message. Only errors fail the options; warnings and hints do not.

Now register a type and validate an instance you only hold as `object`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using Nullean.Entities.Extensions.DependencyInjection;

var services = new ServiceCollection().AddConstraints<SignUp>();
var registry = services.BuildServiceProvider().GetRequiredService<IConstraintRegistry>();

object body = new SignUp("nope");
if (registry.TryValidate(body, out var violations) && violations.Count > 0)
{
	var errors = ViolationErrors.ToDictionary(violations);   // { "Email": ["must be an email address"] }
}

public sealed partial record SignUp(string Email)
{
	[Constraint]
	public static readonly Constraint<SignUp> HasEmail = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]
	);
}
```

`SignUp` is a `partial` record with `[Constraint]` fields, so the source generator in `Nullean.Entities` implements `IHaveConstraints<SignUp>` and `AddConstraints<SignUp>()` finds `SignUp.Constraints`.

## What you get

Register constraints:

- `services.AddConstraints<T>()` where `T : IHaveConstraints<T>`: registers `T.Constraints`.
- `services.AddConstraints(OrderRules.All)`: registers a companion's set (a `ConstraintSet<T>`), for types you cannot make `partial`.
- `services.AddConstraints<T, TFor>(companion)`: registers a generated companion and makes it resolvable as `IConstraintsFor<T>`.

Every call lands in one `ConstraintRegistry` singleton, built when first resolved. It uses the container's `TimeProvider` when one is registered, so time-based rules follow your clock.

Validate options:

- `OptionsBuilder<T>.ValidateConstraints()` for a `T : class, IHaveConstraints<T>`.
- `OptionsBuilder<T>.ValidateConstraints(constraintSet)` for any options class.
- Combine with `ValidateOnStart()` and `BindConfiguration("Section")` to fail fast on a bad `appsettings.json`. A named options builder validates only its own name.

Validate by exact type, without reflection:

- `IConstraintRegistry.IsRegistered(Type)` and `TryValidate(object, out violations)`. `false` means nothing is registered for that type, not that the value is valid.
- `ViolationErrors.ToDictionary(violations)` groups errors into ProblemDetails' `errors` shape: member name (or the rule's id when the rule is not about one member) to messages. It drops warnings and hints.

## Pitfalls and limits

- `TryValidate` looks up the exact runtime type (`instance.GetType()`). A derived type of a registered type is not validated by its base's set.
- A second `AddConstraints` for the same `T` replaces the first in the registry; the last registration wins.
- Constraints are synchronous and pure. A rule that needs a database (is this email taken) does not belong here.
- `ValidateConstraints` for options reports errors only; a warning-level rule never blocks startup.

## Next

- Web endpoints and ProblemDetails: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Command-line options: https://www.nuget.org/packages/Nullean.Entities.Argh
- Integration recipes for ASP.NET Core, options, Marten, EF Core and MassTransit: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Runnable example: https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Integration.Examples
