# Nullean.Entities.CsCheck.AutoBogus

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.AutoBogus.svg)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.AutoBogus)

**Keep your existing `AutoFaker` setup (binders, per-type overrides, skip rules) and still get reproducible, shrinkable property-test data.**

`GenAuto.Of<T>()` in [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck) builds any type itself: lock-free, shrinkable, plausible values mixed with edge cases (blank and hostile strings, NaN, negatives, undefined enums, `Start == End`). This package is the alternative for a team that already configures [`AutoFaker`](https://github.com/soenneker/soenneker.utils.autobogus), or that wants strictly plausible Bogus data. `GenAutoBogus.Of<T>()` returns a CsCheck `Gen<T>` backed by `AutoFaker`, with every draw routed through CsCheck's `PCG`.

It is optional and not the default. It builds on `Nullean.Entities.CsCheck`, depends on `Soenneker.Utils.AutoBogus`, uses reflection, and is not trim or AOT clean. It targets `net10.0` and `net11.0`.

## Install

```bash
dotnet add package Nullean.Entities.CsCheck.AutoBogus
```

It brings `Nullean.Entities.CsCheck` and `Soenneker.Utils.AutoBogus`. Put it in a test project.

## Get going

Draw values and check a property. A failing property reports a seed, and the value shrinks:

```csharp
using CsCheck;
using Nullean.Entities.CsCheck.AutoBogus;

internal sealed record Line(string Sku, int Quantity, decimal Price);

internal sealed record Cart(string Owner, List<Line> Lines);

public class CartTests
{
	[Test]
	public void Owner_is_short()
	{
		// fails on purpose: Bogus names are longer than two characters
		GenAutoBogus.Of<Cart>().Sample(c => c.Owner.Length <= 2, iter: 500);
	}
}
```

The failure is a `CsCheckException` whose message contains `Set seed`: re-run with that seed and you get the same value. The test uses [TUnit](https://tunit.dev) attributes, the framework this repository's tests use.

Configure the `AutoFaker` once; the delegate runs once per thread, on first use:

```csharp
Gen<Cart> carts = GenAutoBogus.Of<Cart>(faker =>
{
	// your AutoFaker configuration: binders, overrides, skip rules
});
```

## The value: your Bogus investment, reproducible

Every draw goes through CsCheck's `PCG`, so values are reproducible by seed and shrink by draw count and magnitude. The `Gen<T>` plugs into any API that takes one, including the constraint helpers in `Nullean.Entities.CsCheck`:

```csharp
using Nullean.Entities;
using Nullean.Entities.CsCheck;

var rules = ConstraintSet.From(("HasOwner", Constraints<Cart>.Always(c => c.Owner.Length > 0, "owner is required")));

rules.ForAll(c => c.Owner.Length > 0, GenAutoBogus.Of<Cart>(), iter: 100);
```

Pass it as `gen:` to `ForAll` and `Probe`, or as `start:` to `Preserves` (`Preserves` also needs the entity's `EntityActions<T>`).

## Which generator to choose

| | `GenAuto.Of<T>()` (default) | `GenAutoBogus.Of<T>()` |
|---|---|---|
| Values | plausible mixed with edge cases | plausible only |
| Rules it can break (25 example domains, 64 rules) | 59 of 64 | 32 of 64 |
| Threads | lock-free, scales | stops scaling at about two threads |
| Existing `AutoFaker` configuration | `AutoOptions.Use<T>(gen)` | kept |
| Some non-public-constructor and abstract types | fails with a clear message | may build them |

Choose this package when the `AutoFaker` configuration is worth keeping. The measurements behind the split are in [`docs/autobogus-decision.md`](https://github.com/nullean/entities/blob/main/docs/autobogus-decision.md).

## What you get

- `GenAutoBogus.Of<T>(Action<AutoFaker>? configure = null)`: a `Gen<T>` that populates every public member of `T` with AutoBogus data, in namespace `Nullean.Entities.CsCheck.AutoBogus`.

## Pitfalls and limits

- **It generates plausible values only.** No NaN, negatives, blank strings or undefined enums, so `Probe` reports most rules as "never violated" and `RejectsInvalid` as unfalsifiable. That is the main reason it is not the default.
- **Bogus's global lock.** `Randomizer.Locker` is an `internal static` taken inside every draw and cannot be disabled, so a multi-threaded CsCheck run stops scaling at about two threads (measured: 71k `Order` draws/second on one thread, 97k on eight, against 617k and 1.6M for `GenAuto.Of<Order>()`).
- **`IndexFaker`/`IndexVariable` are reset to zero before every draw**, so a rule reading `f.IndexFaker` is reproducible. `f.IndexGlobal`, `f.UniqueIndex` and `Faker.GlobalUniqueIndex` are process-wide statics shared with every other `Faker`: avoid them.
- `configure` runs once per thread, not once per sample: keep it pure.
- Output depends on the installed Bogus dataset version, so a Bogus update can change what a seed produces.

## Next

- The default generator and the constraint helpers: [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck)
- Why this is optional, with measurements: https://github.com/nullean/entities/blob/main/docs/autobogus-decision.md
- Generated data reference: https://entities.nullean.net/reference/generated-data/
- Testing API reference: https://entities.nullean.net/reference/testing-api/
- Getting started: https://entities.nullean.net/getting-started/
