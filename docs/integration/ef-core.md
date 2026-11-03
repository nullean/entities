---
navigation_title: EF Core
description: Check constraints on SaveChanges, with EF Core's original values as the before.
---

# EF Core

```bash
dotnet add package Nullean.Entities.EntityFrameworkCore
```

EF entities are changed in place, so `Apply` cannot see a "before". This package adds a `SaveChangesInterceptor` that uses EF's own change tracking instead. On every
`SaveChanges` and `SaveChangesAsync` it checks each tracked entity whose type has a registered set:

| Entry state | What runs |
|---|---|
| `Added` | `Validate(entity)`: the point-in-time rules |
| `Modified` | `Check(original, entity)`: every rule, with the row as EF loaded it as the before, so `Transitions`, `Monotonic` and `Conserves` work |
| `Deleted`, `Unchanged`, `Detached` | Nothing |

When any rule reports an error, it throws one `ConstraintViolationException` with the violations of every offending entity, before EF writes anything.

## Register the sets

```csharp
services.AddDbContext<ShopContext>(options => options
	.UseSqlite(connection)
	.UseConstraints(c => c.Add<Order>()));    // Order declares its rules; or c.Add(OrderRules.All) for a companion's set
```

The rules are ordinary constraints on the entity class:

```csharp
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

```csharp
var order = await db.Orders.SingleAsync(o => o.Id == id);   // Placed, Version 3
order.Status = OrderStatus.Draft;
await db.SaveChangesAsync();                                // throws: status-flow does not allow the transition from Placed to Draft
```

A set registered for a base type also checks the types derived from it. `c.Time` sets the `TimeProvider` the rules get, and `c.OnNotices` receives the warnings and hints of
an entity that saved: they never block a save.

These snippets are tested in
[`tests/Nullean.Entities.EntityFrameworkCore.Tests`](https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.EntityFrameworkCore.Tests) against EF Core on SQLite in
memory: `DependencyInjectionTests.cs` (the registration), `Model.cs` (the entity) and `SaveChangesTests.cs` (the rejected change, sync and async).

## What a rule sees

- **The current entity** is the tracked instance, with whatever navigations are loaded. A rule over a collection (`Unique` over `order.Lines`) sees what is in memory, not the table.
- **The before** comes from `entry.OriginalValues.ToObject()`: a new instance with the scalar properties EF loaded. It has **no navigations**, so a sequential rule that
  compares a collection before and after sees an empty collection before.
- **A change to a child does not modify its parent.** Adding a line to a loaded `order.Lines` leaves the `Order` entry `Unchanged`, so the order's rules do not run. Register
  rules for the child type too, or change a property of the parent (a version column) in the same operation. `NavigationTests.cs` shows both cases.
- **Owned entities** are tracked as entries of their own type: register a set for the owned type to check it.
- Rules run on what is tracked. `ExecuteUpdate`, `ExecuteDelete` and raw SQL bypass the change tracker and are not checked. With `AutoDetectChangesEnabled` off, call
  `DetectChanges` yourself first.

## In a web API

With [ASP.NET Core](aspnetcore.md)'s `AddConstraintProblemDetails()` (and `app.UseExceptionHandler()`), the `ConstraintViolationException` thrown on `SaveChanges` becomes a 400
`ValidationProblem` keyed by `Violation.Member`. When several entities fail, their messages are grouped under the same member names.

## Trim and AOT

The package uses no reflection beyond walking an entity type's base types, and builds without trim or AOT analyzer warnings, but it is not marked AOT compatible: EF Core's
NativeAOT support is experimental (it needs a compiled model and precompiled queries). It is built on EF Core 10 for both `net10.0` and `net11.0`.

## Without the package

The package is one small interceptor
([`ConstraintSaveChangesInterceptor`](https://github.com/nullean/entities/blob/main/src/Nullean.Entities.EntityFrameworkCore/ConstraintSaveChangesInterceptor.cs)). To write
your own, override `SavingChanges` and `SavingChangesAsync`, and for each entry of `ChangeTracker.Entries<Order>()` call `Order.Constraints.Validate(entry.Entity)` when it is
added and `Order.Constraints.Check((Order)entry.OriginalValues.ToObject(), entry.Entity)` when it is modified.

To audit rows already in the database, read them and call `Validate`: [`examples/Apps`](https://github.com/nullean/entities/tree/main/examples/Apps) has an audit of existing rows.
