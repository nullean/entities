---
name: nullean-entities-entityframeworkcore
description: >-
  Use when EF Core entities must obey Nullean.Entities constraints on every save, including transition rules that compare an entity to the row EF loaded. Nullean.Entities.EntityFrameworkCore adds a SaveChanges interceptor that validates added entities, checks modified ones from their original values, and throws before anything is written.
---

# Nullean.Entities.EntityFrameworkCore

`UseConstraints` registers a `ConstraintSaveChangesInterceptor` so every code path that calls `SaveChanges` or `SaveChangesAsync` is covered. It does not query the database (uniqueness against the table belongs in your service); for HTTP 400s add `Nullean.Entities.AspNetCore`.

## The calls you need

```csharp
using Microsoft.EntityFrameworkCore;
using Nullean.Entities;
using Nullean.Entities.EntityFrameworkCore;

services.AddDbContext<ShopContext>(options => options
	// .UseSqlite(connection) or another provider first
	.UseConstraints(c => c.Add<Order>().Add(CustomerRules.All)));
```

```csharp
public sealed partial class Order
{
	public int Id { get; set; }
	public decimal Total { get; set; }
	public OrderStatus Status { get; set; }

	[Constraint]
	public static readonly Constraint<Order> TotalNotNegative = Constraints<Order>.Check(
		o => o.Total >= 0 ? [] : [Violation.Error(nameof(Total), "must not be negative")]);

	[Constraint]
	public static readonly Constraint<Order> StatusFlow = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Draft, OrderStatus.Placed)
		.Allow(OrderStatus.Placed, OrderStatus.Shipped);
}
```

```csharp
try { await db.SaveChangesAsync(); }
catch (ConstraintViolationException e)
{
	foreach (var v in e.Violations) Console.WriteLine($"{v.ConstraintId} {v.Member}: {v.Message}");
}
```

```csharp
options.UseConstraints(c =>
{
	c.Time = clock; // the TimeProvider NotInFuture and NotOlderThan read
	c.OnNotices = (entry, notices) => logger.LogWarning("{Entity}: {Count} notices", entry.Entity, notices.Count);
	c.Add<Order>();
});
IReadOnlyList<Violation> found = new EntityConstraints().Add<Order>().Check(db.Entry(order)); // same check, no save
```

## Gotchas

- Only `Error` violations throw. Warnings and hints go to `OnNotices` and never block a save.
- `Added` runs `Validate` (point-in-time rules only); `Modified` runs `Check(original, current)`, so transition rules see a before. `Deleted` and `Unchanged` are skipped.
- The before is `entry.OriginalValues.ToObject()`: scalar properties only, no navigations. Changing a child does not mark its parent modified, so register rules for the child type too.
- `ExecuteUpdate`, `ExecuteDelete` and raw SQL bypass the change tracker. With `AutoDetectChangesEnabled` off, call `DetectChanges` before saving.
- `Add<T>()` needs `T` to declare its own rules (`[Constraint]` fields on a partial class); otherwise pass a set with `Add(set)`. A set for a base type also covers derived types.

Docs: https://entities.nullean.net
