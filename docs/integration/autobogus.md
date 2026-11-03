---
navigation_title: Bogus and AutoBogus
description: Mix realistic Bogus data into GenAuto's generated values without losing edge cases, seeds or shrinking, or keep an existing AutoFaker setup.
---

# Bogus and AutoBogus

`GenAuto.Of<T>()` builds test data by itself: plausible values mixed with edge cases, relations between fields, reproducible by seed and shrinkable. Where part of a value has to look
real (an address a geocoder accepts, a valid IBAN, a product code in your format), Bogus is the better source for **that part**. You don't have to choose: hand Bogus the parts it is
good at, and `GenAuto` keeps building the rest.

| You want | Use | Package |
|---|---|---|
| Realistic values for one type inside a generated value | `o.Use(GenFakerExtensions.Faker<T>(...))` | `Nullean.Entities.CsCheck` (built in) |
| Your existing `AutoFaker` configuration (binders, overrides, skip rules) | `GenAutoBogus.Of<T>(configure)` | `Nullean.Entities.CsCheck.AutoBogus` |

Either way every Bogus draw goes through CsCheck's `PCG`, so a failing seed replays the same value, and the value still shrinks.

## Richer generation: Bogus for the parts, GenAuto for the whole

`AutoOptions.Use<T>(gen)` replaces the generator for every `T` inside the value with any `Gen<T>`. Give it a Bogus `Faker<T>`:

```csharp
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.AutoBogus;   // only for GenAutoBogus

record Address(string Street, string City, string PostCode);
record BankDetails(string Iban, string Bic);
record Customer(string Name, int LoyaltyPoints, Address Address, BankDetails Bank);

Gen<Customer> customers = GenAuto.Of<Customer>(configure: o => o
    .Use(GenFakerExtensions.Faker<Address>(f => f.CustomInstantiator(x =>
        new Address(x.Address.StreetAddress(), x.Address.City(), x.Address.ZipCode()))))
    .Use(GenAutoBogus.Of<BankDetails>()));
```

Every `Customer` now has a real-looking address and bank details, and its `Name` and `LoyaltyPoints` are still `GenAuto`'s: blank and hostile names, negative and extreme points,
the edge cases that find bugs. Bogus alone would never produce them; `GenAuto` alone would produce an address no validator accepts.

Use it the same way for a rule that `GenAuto` cannot satisfy by luck. If a constraint needs a realistic email, `Probe` or `AutoTest` reports that it produced no valid value and names the
rejecting constraints; `o.Use(GenFakerExtensions.Faker<EmailAddress>(...))` fixes that for that type only.

The example is tested in `tests/Nullean.Entities.CsCheck.AutoBogus.Tests/RicherGenerationTests.cs`: addresses always come from Bogus, names still include blank values, and a seed replays.

## Keep an existing `AutoFaker` setup

```bash
dotnet add package Nullean.Entities.CsCheck.AutoBogus
```

```csharp
using Nullean.Entities.CsCheck.AutoBogus;

Gen<Cart> carts = GenAutoBogus.Of<Cart>(faker =>
{
	// your AutoFaker configuration: binders, overrides, skip rules
});

rules.ForAll(c => c.Owner.Length > 0, carts, iter: 100);
```

The `Gen<T>` plugs into anything that takes one: `gen:` on `ForAll` and `Probe`, `start:` on `Preserves`, or `o.Use<T>(...)` as above. The package brings `Nullean.Entities.CsCheck`
and `Soenneker.Utils.AutoBogus` (a maintained fork of AutoBogus), uses reflection, and is for test projects.

### Which generator to choose for the whole value

| | `GenAuto.Of<T>()` (default) | `GenAutoBogus.Of<T>()` |
|---|---|---|
| Values | plausible mixed with edge cases | plausible only |
| Rules it can break (25 example domains, 64 rules) | 59 of 64 | 32 of 64 |
| Threads | lock-free, scales | stops scaling at about two threads |
| Existing `AutoFaker` configuration | `AutoOptions.Use<T>(gen)` | kept |
| Some non-public-constructor and abstract types | fails with a clear message | may build them |

Plausible-only data cannot break most rules: `Probe` reports them as never violated and the axes as unfalsifiable. That is why the AutoBogus generator is not the default, and why mixing
Bogus in per type, as above, usually beats using it for the whole value. The measurements are in
[`docs/autobogus-decision.md`](https://github.com/nullean/entities/blob/main/docs/autobogus-decision.md).

## Pitfalls

- **Bogus's global lock.** Bogus takes a process-wide lock on every draw (`Randomizer.Locker`, which cannot be turned off). A generator that draws mostly from Bogus stops scaling at
  about two threads; one that uses Bogus for one small part barely notices.
- **`configure` runs once per thread,** on first use, not once per sample: keep it pure.
- **`IndexFaker` and `IndexVariable` are reset to zero before every draw,** so a rule reading them is reproducible but not a useful sequence. `f.IndexGlobal`, `f.UniqueIndex` and
  `Faker.GlobalUniqueIndex` are process-wide statics shared with every other `Faker`: avoid them.
- **The Bogus dataset version is part of the seed.** A Bogus update can change what a seed produces for the parts Bogus draws.

More on what goes through the PCG: [Generated data](../reference/generated-data.md).
