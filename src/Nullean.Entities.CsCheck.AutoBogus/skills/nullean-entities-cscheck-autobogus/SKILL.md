---
name: nullean-entities-cscheck-autobogus
description: >-
  Use when a test project already configures AutoFaker (Soenneker.Utils.AutoBogus) and wants that data in CsCheck property tests.
  GenAutoBogus.Of<T>() wraps AutoFaker as a CsCheck Gen<T> whose draws go through CsCheck's PCG, so they replay by seed and shrink.
---

# Nullean.Entities.CsCheck.AutoBogus

One method, `GenAutoBogus.Of<T>(Action<AutoFaker>? configure = null)`, in namespace `Nullean.Entities.CsCheck.AutoBogus`. It is not the
default: `GenAuto.Of<T>()` from `Nullean.Entities.CsCheck` is lock-free and mixes in edge values, so prefer it unless the `AutoFaker` setup is worth keeping.

## The calls you need

```csharp
// plain CsCheck: a failure throws CsCheckException with a seed to replay
GenAutoBogus.Of<Cart>().Sample(c => c.Lines.Count >= 0, iter: 500);
```

```csharp
// keep your AutoFaker configuration; the delegate runs once per thread
Gen<Cart> carts = GenAutoBogus.Of<Cart>(faker => faker.Config.RecursiveDepth = 1);
```

```csharp
// only the parts that must look real come from Bogus; GenAuto keeps edge values for the rest
Gen<Customer> customers = GenAuto.Of<Customer>(configure: o => o.Use(GenAutoBogus.Of<BankDetails>()));
```

```csharp
// as the generator for the Nullean.Entities.CsCheck helpers
var rules = ConstraintSet.From(("HasOwner", Constraints<Cart>.Always(c => c.Owner.Length > 0, "owner is required")));
rules.ForAll(c => c.Owner.Length > 0, GenAutoBogus.Of<Cart>(), iter: 100);
await AutoTest.For(rules, GenAutoBogus.Of<Cart>()).ReportAsync();
```

## Gotchas

- It generates plausible values only: no NaN, negatives, blank strings or undefined enums. `Probe` then reports most rules as never
  violated and the Vacancy axis finds few counter-examples. Use `GenAuto.Of<T>(AutoMode.Mixed)` when the point is to break rules.
- Every draw takes Bogus's process-wide `Randomizer.Locker`, so a multi-threaded CsCheck run stops scaling at about two threads.
- `configure` runs once per thread on first use, not once per sample. Keep it free of side effects.
- `IndexFaker` and `IndexVariable` are reset before each draw. `IndexGlobal`, `UniqueIndex` and `GlobalUniqueIndex` are process-wide
  statics and break seed replay: do not read them in rules.
- A Bogus dataset update can change what a given seed produces.
- Reflection-based and not trim or AOT clean. Reference it from test projects only.

Docs: https://entities.nullean.net
