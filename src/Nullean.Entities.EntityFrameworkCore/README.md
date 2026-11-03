# Nullean.Entities.EntityFrameworkCore

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Nullean.Entities.EntityFrameworkCore)

**An entity that breaks its rules never reaches the database: `SaveChanges` throws first, and a status that moves backwards is caught too, because the before is the row EF loaded.**

Without this package, an EF entity is changed in place, so `Apply` cannot see a "before", and each service has to remember to check the entity before saving. This package adds a `SaveChangesInterceptor`: on every `SaveChanges` and `SaveChangesAsync` it validates added entities and checks modified ones from EF's original values to their current state, and throws one `ConstraintViolationException` for every offending entity before anything is written.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and `Microsoft.EntityFrameworkCore` 10. It is optional and does not need the other integration packages.

## Install

Targets `net10.0` and `net11.0`, both on EF Core 10 (EF Core 11 is not released yet).

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.EntityFrameworkCore
```

## Get going

Declare the rules on the entity class and register them where you configure the context:

```csharp
using Microsoft.EntityFrameworkCore;
using Nullean.Entities;
using Nullean.Entities.EntityFrameworkCore;

services.AddDbContext<ShopContext>(options => options
	.UseSqlite(connection)
	.UseConstraints(c => c.Add<Order>()));    // or c.Add(OrderRules.All) for a companion's set

public enum OrderStatus { Draft, Placed, Shipped }

public sealed partial class Order
{
	public int Id { get; set; }
	public decimal Total { get; set; }
	public OrderStatus Status { get; set; }
	public int Version { get; set; }

	[Constraint]
	public static readonly Constraint<Order> TotalNotNegative = Constraints<Order>.Check(
		o => o.Total >= 0 ? [] : [Violation.Error(nameof(Total), "must not be negative")]);

	[Constraint]
	public static readonly Constraint<Order> StatusFlow = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Draft, OrderStatus.Placed)
		.Allow(OrderStatus.Placed, OrderStatus.Shipped);

	[Constraint]
	public static readonly Constraint<Order> VersionOnlyGrows = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}
```

Then a change that breaks a rule throws, and the row stays as it was:

```csharp
var order = await db.Orders.SingleAsync(o => o.Id == id);   // Placed, Version 3
order.Status = OrderStatus.Draft;
await db.SaveChangesAsync();
// ConstraintViolationException: SaveChanges rejected, constraints violated. Order (Modified): status-flow does not allow the transition from Placed to Draft.
```

The value: the rule is written once, on the entity, and every code path that saves it is covered, including the ones written next year. With `Nullean.Entities.AspNetCore`'s `AddConstraintProblemDetails()`, the exception becomes a 400 `ValidationProblem` with no try/catch.

## What you get

- `optionsBuilder.UseConstraints(c => ...)` on `DbContextOptionsBuilder` and `DbContextOptionsBuilder<TContext>`: adds a `ConstraintSaveChangesInterceptor`.
- `EntityConstraints`, the `c` above:
  - `Add<T>()` for an entity that declares its own rules (`[Constraint]` fields or `[Constraints]`), `Add<T>(constraintSet)` for any set.
  - `Time`: the `TimeProvider` the rules get (`NotInFuture`, `NotOlderThan`).
  - `OnNotices`: called with the entry and its warnings and hints when an entity saved; they never block.
  - `Check(entry)`: the violations of one `EntityEntry`, for code that wants the same check without saving.
- What runs: `Validate` for an `Added` entity, `Check(original, current)` for a `Modified` one, nothing for `Deleted` or `Unchanged`. A set registered for a base type also checks derived types.

## Pitfalls and limits

- The before is `entry.OriginalValues.ToObject()`: scalar properties only, **no navigations**. A sequential rule comparing a collection sees it empty before.
- A rule over a collection sees what is loaded, not the table.
- Adding or changing a child does not modify its parent, so the parent's rules do not run. Register rules for the child type too, or touch a property of the parent (a version column).
- `ExecuteUpdate`, `ExecuteDelete` and raw SQL bypass the change tracker and are not checked. With `AutoDetectChangesEnabled` off, call `DetectChanges` first.
- Rules are synchronous and pure: "is this email taken" is a query for your service, not a constraint.
- Sets are registered on the interceptor, not read from `IConstraintRegistry`: that registry validates an `object` but cannot run the before-and-after check a modified entity needs.
- Not marked trim or AOT compatible. The package itself builds without trim warnings, but EF Core's NativeAOT support is experimental.

## Next

- This integration on the docs site: https://entities.nullean.net/integration/ef-core/, and every integration: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Web APIs: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.EntityFrameworkCore.Tests
