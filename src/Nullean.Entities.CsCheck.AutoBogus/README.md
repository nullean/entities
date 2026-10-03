# Nullean.Entities.CsCheck.AutoBogus

An [AutoBogus](https://github.com/soenneker/soenneker.utils.autobogus)-backed generator for
[`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck). **Optional, and not the default.**

`GenAuto.Of<T>()` in the main package builds any type itself: lock-free, shrinkable, plausible values mixed with edge cases (blank and
hostile strings, NaN, negatives, undefined enums, `Start == End`). Use **this** package when you already configure `AutoFaker` (binders, per-type
overrides, skip rules) and want to keep that investment, or when you want strictly plausible Bogus data.

```csharp
using Nullean.Entities.CsCheck.AutoBogus;

Gen<Order> orders = GenAutoBogus.Of<Order>(faker => { /* your AutoFaker configuration: binders, overrides, skip rules */ });

OrderConstraints.All.ForAll(o => o.Total >= 0, gen: orders);                 // any API that takes a Gen<T> takes this
OrderConstraints.All.Preserves(OrderConstraints.Actions, start: orders);
OrderConstraints.Instance.Spec().Data(AutoMode.Plausible);                   // plausible start/argument data without this package
```

Every draw goes through CsCheck's `PCG`, so values are reproducible by seed and shrink by draw count and magnitude.

## What to know

- **It generates plausible values only.** No NaN, negatives, blank strings or undefined enums, so `Probe` reports most rules as "never violated"
  and `RejectsInvalid` as unfalsifiable. That is the main reason it is not the default.
- **Bogus's global lock.** `Randomizer.Locker` is an `internal static` taken inside every draw and cannot be disabled, so a multi-threaded CsCheck
  run stops scaling at about two threads (measured: 71k `Order` draws/second on one thread, 97k on eight, against 617k and 1.6M for `GenAuto.Of<Order>()`).
- **`IndexFaker`/`IndexVariable` are reset to zero before every draw**, so a rule reading `f.IndexFaker` is reproducible. `f.IndexGlobal`,
  `f.UniqueIndex` and `Faker.GlobalUniqueIndex` are process-wide statics shared with every other `Faker`: avoid them.
- `configure` runs once per thread, not once per sample: keep it pure.

The measurements behind this split are in
[`docs/autobogus-decision.md`](https://github.com/nullean/entities/blob/main/docs/autobogus-decision.md).

## Install

```sh
dotnet add package Nullean.Entities.CsCheck.AutoBogus
```

License: MIT
