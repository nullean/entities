---
navigation_title: Integrations
description: Where to call a constraint set in a real service, which integrations are packages, and which are recipes.
---

# Using {{product}} in a real service

The library has no opinion about your framework: a constraint set is a value you call at the edge of your domain. Some integrations are a package you add; others are a few lines of your own code, shown as a recipe.

## Packages and recipes

| Integration | How | Page |
|---|---|---|
| ASP.NET Core: endpoint filters, MVC, ProblemDetails | Package `Nullean.Entities.AspNetCore` | [ASP.NET Core](aspnetcore.md) |
| Dependency injection and `IOptions<T>` validation | Package `Nullean.Entities.Extensions.DependencyInjection` | [Options and DI](options.md) |
| [Nullean.Argh](https://github.com/nullean/argh) command lines | Package `Nullean.Entities.Argh` | [Command-line apps](argh.md) |
| TUnit, xUnit, NUnit and MSTest: one test per axis | Built into `Nullean.Entities.CsCheck` (`AsDataSource()`, `CaseNames`), no framework dependency | [Test frameworks](test-frameworks.md) |
| Logging and metrics (`ILogger`, `System.Diagnostics.Metrics`, OpenTelemetry) | Package `Nullean.Entities.Diagnostics` | [Logging and metrics](diagnostics.md) |
| Blazor `EditForm` validation | Package `Nullean.Entities.Blazor` | [Blazor forms](blazor.md) |
| MVVM view models (`INotifyDataErrorInfo`): Avalonia, WPF, WinUI, MAUI | Package `Nullean.Entities.ComponentModel` | [MVVM](mvvm.md) |
| ReactiveUI view models (ReactiveUI.Validation) | Package `Nullean.Entities.ReactiveUI` | [ReactiveUI](reactiveui.md) |
| FluentValidation: constraints as validators, validators as constraints | Package `Nullean.Entities.FluentValidation` | [FluentValidation](fluentvalidation.md) |
| DataAnnotations (`[Required]`, `[Range]`, ...) | Built in: the source generator in `Nullean.Entities`, and `GenAuto` in `Nullean.Entities.CsCheck` | [DataAnnotations](data-annotations.md) |
| Bogus and AutoBogus test data | `GenFakerExtensions.Faker<T>` is built into `Nullean.Entities.CsCheck`; package `Nullean.Entities.CsCheck.AutoBogus` | [Bogus and AutoBogus](autobogus.md) |
| Models that hold files (`System.IO.Abstractions`) | Packages `Nullean.Entities.FileSystem` and `Nullean.Entities.CsCheck.FileSystem` | [File systems](file-system.md) |
| System.Reactive event streams | Packages `Nullean.Entities.Reactive` and `Nullean.Entities.CsCheck.Reactive` | [System.Reactive](reactive.md) |
| Marten and event sourcing | Recipe, compiled in `examples/` | [Marten](marten.md) |
| EF Core | Recipe, not compiled here | [EF Core](ef-core.md) |
| MassTransit sagas and consumers | Recipe, not compiled here | [MassTransit](masstransit.md) |

Which parts are tested: the **library side** of every recipe is tested in `tests/Nullean.Entities.Examples.Tests/Integration`, and the ASP.NET Core and Marten-style
recipes compile and run as `examples/Nullean.Entities.Integration.Examples`. The **EF Core and MassTransit** snippets use those packages' APIs, but this repository does not
reference those packages, so they are **not compiled here**: treat them as the shape, and check the signatures against the version you use.

## What to put where

| Concern | Where the rule goes |
|---|---|
| Invariants of one aggregate (a total is never negative, a state change is legal, nothing is created or destroyed) | A `ConstraintSet<T>` on the aggregate (`[Constraints]`), checked in the domain |
| Rules over a collection inside the aggregate (unique ids, dangling references, cycles) | `Unique`, `References`, `Acyclic`, `NoChains` in the same set |
| Rules that need a database (is this email taken, does this customer exist in another service) | **Not here.** Constraints are synchronous and pure on purpose; do those in the application service |
| What must happen over time (publish only after validation, reply within 10 minutes) | `Temporal<TEvent>` rules, checked over an event log or a live stream |
| "Does my implementation follow the lifecycle diagram?" | `StateMachine<,>` and `ConformTo` in a test |

Rules over two aggregates, rules on the declared or the resolved layer, and severities are covered in [Rules over real models](../guides/real-models.md).

## Trim and AOT

`Nullean.Entities`, `Nullean.Entities.Reactive`, `Nullean.Entities.FileSystem`, `Nullean.Entities.Extensions.DependencyInjection`, `Nullean.Entities.AspNetCore`,
`Nullean.Entities.Argh`, `Nullean.Entities.Diagnostics` and `Nullean.Entities.ComponentModel` use no reflection and are trim and NativeAOT clean. `Nullean.Entities.Blazor` uses no reflection and builds with the trim and AOT analyzers on. The first four, `Nullean.Entities.Diagnostics` and `Nullean.Entities.ComponentModel`, are published NativeAOT in this repository's CI (`./build.sh aot`).
The `Nullean.Entities.CsCheck` packages are for tests only and are not AOT-compatible.
`Nullean.Entities.ReactiveUI` uses no reflection itself, but ReactiveUI.Validation is not trim or AOT clean (`ValidationContext` and `BindValidation` require unreferenced and dynamic code), so neither is an app that uses it.

## Complete, runnable versions

The recipes are fragments. [`examples/Apps`](https://github.com/nullean/entities/tree/main/examples/Apps) has eight console apps that run end to end and are tested like a real service would be: an order service on a type-state machine, a bank transfer, a support desk on virtual time, an event-sourced cart (the Marten pattern, in memory), inventory across two aggregates, an audit of existing rows, a hierarchy edited as a tree, and a docs build over a file system. See [`docs/first-use-review.md`](https://github.com/nullean/entities/blob/main/docs/first-use-review.md) for what building them found.
