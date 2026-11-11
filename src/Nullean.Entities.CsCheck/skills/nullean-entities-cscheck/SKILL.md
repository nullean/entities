---
name: nullean-entities-cscheck
description: >-
  Use when writing property tests for a type or service whose rules are declared with Nullean.Entities. AutoTest turns
  each declared rule into a named, shrinking, seed-replayable test per axis, and GenAuto builds a CsCheck Gen<T> from a type's shape.
---

# Nullean.Entities.CsCheck

Test-time package: it reads a `ConstraintSet<T>` or `[Constraints]` companion and picks the test from each rule's shape. Keep it out of
production code (it uses reflection); for `IObservable<T>` services add `Nullean.Entities.CsCheck.Reactive`.

## The calls you need

```csharp
// every declared axis in one call; throws one exception listing each failed axis
Gen<Order> orders = Gen.Select(Gen.Int[0, 2], Gen.Int[0, 5], (s, v) => new Order((OrderStatus)s, v));
await AutoTest.For(OrderConstraints.Instance, orders).RunAsync();
```

```csharp
// one test per axis: feed CaseNames to xUnit [MemberData], NUnit [TestCaseSource] or MSTest [DynamicData]
var auto = AutoTest.For(OrderConstraints.Instance, orders);
foreach (var name in auto.CaseNames)
	await auto.Case(name).RunAsync();
```

```csharp
// replay a failure, or read the result instead of throwing
var report = await auto.Seed("5gGXNkqJNXB1").Samples(500).RequireAxes(Axis.Monotonicity).ReportAsync();
Console.WriteLine(report);
```

```csharp
// a generator from the shape of a type, filtered to valid values
Gen<Order> mixed = GenAuto.Of<Order>(AutoMode.Mixed, o => o.Use(Gen.Const(OrderStatus.Pending)));
mixed.Valid(OrderConstraints.All).Sample(o => o.Version >= 0);
```

```csharp
// a class changed in place: a fresh instance per run, rules lifted onto it with Map
await AutoTest.For(walletRules, () => new Wallet())
	.MutateAsync<int>("Move", (w, amount) => w.MoveAsync(amount), _ => Gen.Int[1, 12])
	.Steps(1, 6)
	.RunAsync();
```

## Gotchas

- `AutoTest` without a generator draws `AutoMode.Plausible` data; `GenAuto.Of<T>()` and `rules.ForAll(...)` default to `Mixed`.
  Opt into hostile data with `.Data(AutoMode.Mixed)`.
- The seed is fixed by default; `.Seed(...)` replays the one a failure printed.
- An axis with no rule is "not applicable", never a pass and never a failure. Use `.RequireAxes(...)` to make it fail.
- `RunAsync()` and `AxisCase.RunAsync()` throw `InvalidOperationException`; `ReportAsync()` never throws on a failed axis.
- With TUnit use `AsDataSource()` and a test taking `AxisCase`. `Case(name)` throws `ArgumentException` when discovery and run build
  the `AutoTest` differently, so build it in one static method.
- `Mutate`, `MutateAsync` and `Steps` throw unless the `AutoTest` came from a factory. Records use `Action(...)`.
- Factories and steps must be deterministic, and read time only from the `TimeProvider` (`For(rules, clock => new X(clock))`).
- Vacancy fails when a rule is never exercised or has no counter-example; add one with `.CounterExample(rule, breakIt)`.
- `Valid(...)` is a filter: a mostly invalid generator fails, naming the rules that rejected it.

Docs: https://entities.nullean.net
