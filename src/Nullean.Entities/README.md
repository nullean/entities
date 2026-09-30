# Nullean.Entities

Synchronous domain constraints for .NET: name a rule once, check it against a value or a proposed
mutation. No `System.Reactive`, no `CsCheck` — this package has no dependency beyond the BCL.

## The idea

A `Constraint<T>` is a named rule:

```csharp
var alwaysPositive = new Constraint<Item>(
    "AlwaysPositive",
    "Value must stay positive.",
    item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
);
```

It lives anywhere — a `static readonly` field, a local variable, a constructor parameter. A
`ConstraintSet<T>` is an ordered collection of them:

```csharp
var constraints = ConstraintSet.From(alwaysPositive, neverOverHundred);

IReadOnlyList<Violation> violations = constraints.Validate(new Item(-1));
// violations[0].ConstraintId == "AlwaysPositive"
```

## Safe mutation

`Apply` runs a mutation, checks every constraint against the *result*, and only commits if all of
them pass:

```csharp
Result<Item> result = constraints.Apply(current, item => item with { Value = item.Value + 1 });

var next = result.Match(
    ok => ok,
    (attempted, violations) => throw new InvalidOperationException(violations[0].Message)
);
```

`ApplyOrThrow` is the same thing without the `Match`. `ConstraintChannel<T>` wraps a single mutable
`Current` value behind the same `Apply` semantics, so a caller can keep proposing mutations without
re-plumbing the constraint set each time:

```csharp
var channel = ConstraintChannel.Enforce(new Item(1), constraints);
channel.Apply(item => item with { Value = 5 });   // Current is now 5
channel.Apply(item => item with { Value = -1 });  // rejected; Current is still 5
```

## `IHaveConstraints<TSelf>`

A type can declare its own constraint set as a static member, using a C# 11 static abstract
interface member:

```csharp
public sealed partial record Item(int Value) : IHaveConstraints<Item>
{
    public static ConstraintSet<Item> Constraints { get; } = ConstraintSet.From(
        new Constraint<Item>("AlwaysPositive", "...", i => i.Value > 0 ? [] : [Violation.Error("...")])
    );
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
