# Nullean.Entities

Synchronous domain constraints for .NET: name a rule once, check it against a value or a proposed
mutation. No `System.Reactive`, no `CsCheck` — this package has no dependency beyond the BCL.

## The idea

A `Constraint<T>` is a named rule, built through `Constraints<T>` rather than a public constructor:

```csharp
var alwaysPositive = Constraints<Item>.Always(item => item.Value > 0, "Value must stay positive.");
```

It lives anywhere — a `static readonly` field, a local variable, a constructor parameter. A
`ConstraintSet<T>` is an ordered collection of them, named from the tuple you build it with:

```csharp
var constraints = ConstraintSet.From((nameof(alwaysPositive), alwaysPositive), (nameof(neverOverHundred), neverOverHundred));

IReadOnlyList<Violation> violations = constraints.Validate(new Item(-1));
// violations[0].ConstraintId == "alwaysPositive"
```

`Always(predicate, message)` covers the common fixed-message case. `Constraints<T>.Check(check)` is
the escape hatch for a message built from the value itself:

```csharp
var alwaysPositive = Constraints<Item>.Check(
    item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
);
```

## Safe mutation

`Apply` runs a mutation, checks every constraint against the result, and only commits if all of
them pass:

```csharp
Result<Item> result = constraints.Apply(current, item => item with { Value = item.Value + 1 });

var next = result.Match(
    ok => ok,
    violations => throw new InvalidOperationException(violations[0].Message)
);
```

`ApplyOrThrow` is the same thing without the `Match`. `ConstraintChannel<T>` wraps a single mutable
`Current` value behind the same `Apply` semantics, so a caller can keep proposing mutations without
re-plumbing the constraint set each time:

```csharp
var channel = ConstraintChannel.For(new Item(1)).Enforce(constraints);
channel.Apply(item => item with { Value = 5 });   // Current is now 5
channel.Apply(item => item with { Value = -1 });  // rejected; Current is still 5
```

## Exhaustive constraint sets: `[Constraint]`

Tag every `Constraint<T>` field or property that belongs to one type with `[Constraint]`, and
[`Nullean.Entities.SourceGenerators`](../Nullean.Entities.SourceGenerators) (bundled into this
package's own nupkg as an analyzer — no separate install) bundles them into a `Constraints`
property automatically:

```csharp
public sealed partial record Item(int Value)
{
    [Constraint]
    public static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(i => i.Value > 0, "...");

    [Constraint]
    public static readonly Constraint<Item> NeverOverHundred = Constraints<Item>.Always(i => i.Value <= 100, "...");

    // Generated: public static ConstraintSet<Item> Constraints { get; } =
    //     ConstraintSet.From((nameof(AlwaysPositive), AlwaysPositive), (nameof(NeverOverHundred), NeverOverHundred));
}
```

A type whose own `[Constraint]` fields target itself also gets `IHaveConstraints<T>` added to its
partial declaration for free, so `Item.Constraints` and generic code written against
`IHaveConstraints<T>` both just work. A separate "spec" companion class tagging someone else's type
gets the plain property with no interface. The set can never drift from the fields actually
declared — there is no separate list to keep in sync, and adding a field updates it automatically.

Any misused `[Constraint]` member — not static, wrong type, not partial, nested, generic, or an
inconsistent type argument across members on the same type — refuses to generate anything for that
type at all, rather than silently generating from the valid subset. See
[the generator's own README](../Nullean.Entities.SourceGenerators/README.md) for the full rules.

## Sequential constraints: `Transitions`

`Always` and `Check` only ever see the value being checked. A constraint that depends on what the
value *was* — a legal state-machine transition, a value that must never decrease — needs
`Transitions`, which only `Apply` (never `Validate`) can run, since a standalone instance has no
"previous" to compare against:

```csharp
var validTransitions = Constraints<Order>.Transitions(
    (prev, next) => (prev.Status, next.Status) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
        (_, OrderStatus.Cancelled) => true,
        _ => false,
    },
    "Illegal order status transition."
);
```

`When(guard).Requires(requirement)` adds a conditional constraint — violated only when `guard` is
true for the value but `requirement` fails:

```csharp
var paymentRefRequired = Constraints<Order>
    .When(o => o.Status == OrderStatus.Confirmed)
    .Requires(o => o.PaymentRef != null, "PaymentRef is required once an order is confirmed.");
```

## `IHaveConstraints<TSelf>`

A type can declare its own constraint set as a static member, using a C# 11 static abstract
interface member — this is what `[Constraint]` generates for you, but it can be written by hand
too:

```csharp
public sealed partial record Item(int Value) : IHaveConstraints<Item>
{
    private static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(i => i.Value > 0, "...");

    public static ConstraintSet<Item> Constraints { get; } = ConstraintSet.From((nameof(AlwaysPositive), AlwaysPositive));
}
```

This is what [`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive)
and [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck) attach to
without this package depending on either of them.

## Install

```sh
dotnet add package Nullean.Entities
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — lifts a
  `ConstraintSet<T>` onto an `IObservable<TEvent>` stream, plus temporal/structural operators that
  only make sense against a stream.
- License: MIT
