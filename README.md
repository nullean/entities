<p align="center">
  <img src="docs/images/lockup.svg" width="276" height="96" alt="Nullean.Entities">
</p>

<p align="center">Executable specifications for .NET: declare a type's rules once, next to it, and the same declaration validates values, is tested on generated data, and watches live events.</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Nullean.Entities"><img src="https://img.shields.io/nuget/v/Nullean.Entities?style=flat-square" alt="NuGet"></a>
  <a href="https://github.com/nullean/entities/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/nullean/entities/ci.yml?branch=main&style=flat-square&label=ci" alt="CI"></a>
  <a href="https://entities.nullean.net"><img src="https://img.shields.io/badge/docs-entities.nullean.net-20a0a0?style=flat-square" alt="Docs"></a>
</p>

![Nullean.Entities: executable specifications for .NET](docs/images/png/entities-social-card.png)

**Types hold data. Nothing constrains it beyond its type.**

A well-typed `Order` can still have a negative total, ship after it was cancelled, or lose a version. C# records and unions say what an order holds and which cases exist. They cannot say that a total is never negative, that cancelling twice is cancelling once, that a version never goes back, or that a confirmed order ships within a minute. Those rules end up in `if` statements, validators and people's heads, written again for each use, and they drift apart.

Nullean.Entities makes them **first-class**: a rule is a named value, declared once next to the type. The same declaration validates a value, is tested automatically on generated data, and watches live events. And the tests it writes ask nine questions most test suites never ask.

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.CsCheck   # tests only
```

Every package targets `net10.0` and `net11.0`.

## Quick start

```csharp
[Constraints<Order>]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}
```

```csharp
await AutoTest.For(OrderConstraints.Instance, orders).RunAsync();   // one test per axis the rules declare
```

When a method of `Order` resets the version, you read:

```text
Monotonicity failed.

Rule broken   version-never-goes-back: Expected NonDecreasing but moved from 1 to 0.
Operation     Reset()
Started from  Order { Lines = [], Status = Cancelled, Version = 1, Total = 0, Label = "" }
Produced      Order { Lines = [], Status = Cancelled, Version = 0, Total = 0, Label = "" }

Law           No operation moves a value that may only go one way in the other direction.
Consequence   An older result overwrites a newer one, a counter goes back or a timestamp regresses, without anything saying so.

Smallest case found, after 3 shrinks. To run this case again: .Seed("2pERrTiV-JM9")
```

The example is [`examples/Nullean.Entities.Quickstart`](examples/Nullean.Entities.Quickstart), and its tests plant this bug and five more.

## Why

**Rules in one place.** A rule is a named value next to the type. `[Constraints]` gathers them into one set, so a rule you add cannot be forgotten.

**Tests you do not write.** The shape of a rule picks the test: `Idempotent` runs the action twice, `Monotonic` runs the operations and compares before and after, a deadline rule is tried with delays either side of the deadline.

**A test that proves it ran.** If the generated data never reached a rule, the test fails as vacuous instead of passing quietly. A failure is the smallest case that breaks the rule, and a seed that replays it.

**A way in for existing code.** Rules start as warnings and become errors on a date. See [adopting in an existing codebase](docs/guides/adopting.md).

## The nine questions

The shape of each rule tells the library what to test. These are questions a scenario test almost never covers:

| | The question | The bug it catches |
|---|---|---|
| [01](docs/axes/01-invariant.md) | Is it true in every state? | an operation that lets a bad value in |
| [02](docs/axes/02-idempotency.md) | Does doing it twice change anything? | a retry that changes state twice |
| [03](docs/axes/03-monotonicity.md) | Does it ever go backwards? | an older result overwriting a newer one |
| [04](docs/axes/04-conservation.md) | Did anything appear or vanish? | totals that drift |
| [05](docs/axes/05-absence.md) | Can this bad sequence ever happen? | a cancelled order that still ships |
| [06](docs/axes/06-temporal.md) | Does B follow A within a deadline? | a promise missed by one unlucky gap |
| [07](docs/axes/07-liveness.md) | Does it eventually finish? | the endless spinner |
| [08](docs/axes/08-confluence.md) | Does the order inputs arrive in matter? | two rebuilds publishing different output |
| [09](docs/axes/09-vacancy.md) | Did the test even reach the rule? | a green test that checked nothing |

## Packages

| Package | What it adds | |
|---|---|---|
| [`Nullean.Entities`](src/Nullean.Entities/README.md) | `Constraint<T>`, `[Constraints]`, `Temporal<T>` rules, state machines. No other dependency. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities) |
| [`Nullean.Entities.CsCheck`](src/Nullean.Entities.CsCheck/README.md) | `AutoTest`, `EventAutoTest`, `Script` on a `TimeProvider`, generated data. Tests only. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.CsCheck) |
| [`Nullean.Entities.Argh`](src/Nullean.Entities.Argh/README.md) | *Optional.* Validate the option objects a Nullean.Argh command receives; violations are `--flag` errors with exit code 2. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Argh?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Argh) |
| [`Nullean.Entities.AspNetCore`](src/Nullean.Entities.AspNetCore/README.md) | *Optional.* Answer constraint violations in ASP.NET Core with a `ValidationProblem`: endpoint filters, an exception handler, result mapping and an MVC filter. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.AspNetCore?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.AspNetCore) |
| [`Nullean.Entities.Blazor`](src/Nullean.Entities.Blazor/README.md) | *Optional.* A Blazor `EditForm` validator, so `ValidationMessage` and `ValidationSummary` show violations per field. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Blazor?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Blazor) |
| [`Nullean.Entities.ComponentModel`](src/Nullean.Entities.ComponentModel/README.md) | *Optional.* Violations through `INotifyDataErrorInfo` for MVVM view models (Avalonia, WPF, WinUI, MAUI). | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.ComponentModel?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.ComponentModel) |
| [`Nullean.Entities.CsCheck.AutoBogus`](src/Nullean.Entities.CsCheck.AutoBogus/README.md) | *Optional.* Real-world data from an AutoFaker. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.AutoBogus?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.AutoBogus) |
| [`Nullean.Entities.CsCheck.FileSystem`](src/Nullean.Entities.CsCheck.FileSystem/README.md) | *Optional.* Rules over files and generated file-system worlds. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.FileSystem?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.FileSystem) |
| [`Nullean.Entities.CsCheck.Reactive`](src/Nullean.Entities.CsCheck.Reactive/README.md) | *Optional.* The same scripts on an Rx scheduler. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.Reactive?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive) |
| [`Nullean.Entities.Diagnostics`](src/Nullean.Entities.Diagnostics/README.md) | *Optional.* Violations seen at runtime as logs, metrics and trace events (OpenTelemetry picks them up by name; no dependency on it). | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Diagnostics?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Diagnostics) |
| [`Nullean.Entities.EntityFrameworkCore`](src/Nullean.Entities.EntityFrameworkCore/README.md) | *Optional.* Check added and modified entities on `SaveChanges`, with EF Core's original values as the before. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.EntityFrameworkCore?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.EntityFrameworkCore) |
| [`Nullean.Entities.Extensions.DependencyInjection`](src/Nullean.Entities.Extensions.DependencyInjection/README.md) | *Optional.* Register constraint sets with Microsoft.Extensions.DependencyInjection and validate `IOptions` with them. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Extensions.DependencyInjection?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection) |
| [`Nullean.Entities.FileSystem`](src/Nullean.Entities.FileSystem/README.md) | *Optional.* Rules over files and generated file-system worlds. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.FileSystem?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.FileSystem) |
| [`Nullean.Entities.FluentValidation`](src/Nullean.Entities.FluentValidation/README.md) | *Optional.* Run constraints inside FluentValidation pipelines, and attack existing FluentValidation validators with generated data. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.FluentValidation?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.FluentValidation) |
| [`Nullean.Entities.Reactive`](src/Nullean.Entities.Reactive/README.md) | *Optional.* Constraints over `IObservable<T>`. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Reactive?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Reactive) |
| [`Nullean.Entities.ReactiveUI`](src/Nullean.Entities.ReactiveUI/README.md) | *Optional.* Show violations on a ReactiveUI view model through ReactiveUI.Validation. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.ReactiveUI?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.ReactiveUI) |
| [`Nullean.Entities.SystemCommandLine`](src/Nullean.Entities.SystemCommandLine/README.md) | *Optional.* Validate the options a System.CommandLine command binds; violations are parse errors that name the option. | [![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.SystemCommandLine?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.SystemCommandLine) |

## Learn more

- Documentation: https://entities.nullean.net, with [why](docs/why.md), [getting started](docs/getting-started/index.md),
  [the nine axes](docs/axes/index.md) and [concepts in plain words](docs/concepts.md)
- Building from source: `mise run build` and `mise run test`, or `./build.sh build` and `./build.sh test`. The .NET 11 SDK
  is pinned in `global.json` and `mise install` provides it. [AGENTS.md](AGENTS.md) describes the build targets,
  package layout and conventions.
- [Releases](https://github.com/nullean/entities/releases)
- [Contributing](https://github.com/nullean/.github/blob/main/CONTRIBUTING.md)

MIT licensed, see [LICENSE.txt](LICENSE.txt). Copyright Nullean and contributors.
