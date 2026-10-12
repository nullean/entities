# Nullean.Entities.CsCheck

Test your domain from the rules you declare on it, on nine axes you have probably never considered. Built on [CsCheck](https://github.com/AnthonyLloyd/CsCheck).

[CsCheck](https://github.com/AnthonyLloyd/CsCheck) is a very good property-testing library, and this package does not replace it. Every generator here is a CsCheck `Gen<T>`, every failure is a CsCheck failure (a shrunk case and a seed that replays it), and you can mix the two in one test class. If you are testing an algorithm or a pure function, use CsCheck on its own.

If you have a domain with rules (a total is never negative, cancelling twice is cancelling once, a confirmed order ships within a minute), this package turns those rules into tests.

## What it adds to CsCheck

| With plain CsCheck | With this package |
|---|---|
| You write a generator for each type | `GenAuto` builds one from the shape of the type, with hostile values (blank strings, `NaN`, undefined enums, duplicate ids) mixed in by default |
| You write a property for each rule | You declare the rule (`Idempotent`, `Monotonic`, `OrderIndependent`, a deadline); its shape picks the property and the inputs |
| A property that passes | A check that the generated data **reached the rule**; a rule never exercised fails as vacuous |
| Counter-examples you think of | A counter-example per rule to show it can fire, found by search for most |
| Operations you list | The operations of your type, discovered, with arguments harvested from the value they act on |
| No clock | A virtual clock, and delays chosen from your deadlines |

Read more: [what this adds to CsCheck](../../docs/why-not/cscheck.md), with the same tests written both ways.

## One screen

```csharp
// the rules, declared once next to the type
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());
	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}

// the tests: one per axis the rules declare
public static IEnumerable<Func<AxisCase>> Axes() => AutoTest.For(OrderConstraints.Instance, orders).AsDataSource();

[Test, MethodDataSource(nameof(Axes))]
public void EveryDeclaredAxisHolds(AxisCase axis) => axis.Run();
```

## Install

```sh
dotnet add package Nullean.Entities.CsCheck   # tests only
```

The package does not depend on System.Reactive. If your service is built on `IObservable<T>`, add `Nullean.Entities.CsCheck.Reactive`.

## Where to go next

- [Getting started](../../docs/getting-started/index.md): five minutes, one order, one red test.
- [Why](../../docs/why.md) and [concepts in plain words](../../docs/concepts.md).
- [The nine axes](../../docs/axes/index.md) and [the vocabulary](../../docs/design-principles/vocabulary.md).
- [Reference](../../docs/reference/testing-api.md): the generators, `Script`, model-based and metamorphic testing, state machines, generated data.
- [Source](https://github.com/nullean/entities) and [AGENTS.md](../../AGENTS.md).

License: MIT.
