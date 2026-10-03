# Should Nullean.Entities.CsCheck keep AutoBogus?

**Recommendation: stop depending on it by default. Keep `GenAuto.Bogus<T>` only as an opt-in package, and drop it entirely if
nobody asks for it within a release.** This is the case for that, from measurements on this repo's own domains. The decision is yours.

What exists today: `GenAuto.Of<T>()` is a new generator (`AutoBuilder`) that builds objects from their shape and draws every leaf from
CsCheck's `PCG`. Bogus is used once, as a word list. `GenAuto.Bogus<T>()` is the old AutoBogus-backed generator, unchanged, and nothing
has been removed. Both ran on all 25 example constraint sets (64 point-in-time rules) with the same seeds.

## 1. It breaks far more rules

For each rule, 1,000 generated values, counting whether the generator ever produced a value that **violated** it and one that **satisfied** it.

| | AutoBogus | `GenAuto.Of<T>()` (Mixed, the default) |
|---|---|---|
| Rules the generator can **break** | **32 of 64 (50%)** | **59 of 64 (92%)** |
| Rules the generator can **satisfy** | 56 of 64 (88%) | **64 of 64 (100%)** |

AutoBogus only makes plausible data: positive decimals, words, in-range dates. Half the rules in the example domains could never be
violated by it, so `Probe` and `RejectsInvalid` reported them as "unfalsifiable" and the tests needed hand-made generators (the Money
and Temperature tests had them). With the new generator those hand-made generators were deleted. The 5 rules the default still cannot
break are overlap and rest-period rules over lists of time slots (`Roster`, `BookingCalendar`): structured relations, not leaf values.

Real findings the new default produced that AutoBogus never could (all in `AUTOTEST_NOTES.md`): `Order.Total` overflows `decimal`
on extreme lines; `BankAccount.Deposit` accepts negative amounts and does nothing at ~7.9e28; `TrafficLight.Advance()` throws on an
undefined `Signal`; no domain rejects an undefined enum; constraints that **throw** instead of reporting on hostile input.

## 2. It builds valid values where AutoBogus cannot

Share of 1,000 generated values that already satisfy every rule (so they can be used as a starting state):

| Type (tight rules) | AutoBogus | `GenAuto.Of<T>()` |
|---|---|---|
| `EmailAddress` (needs `@` and `.`) | 0.0% | 0.2% |
| `JournalEntry` (must balance) | 0.0% | 0.4% |
| `VitalSigns` (six plausibility ranges) | 0.0% | 0.0% (needs a hand rule) |
| `PatientRecord` | 0.0% | 2.3% |
| `OrgChart` (referential integrity) | 0.0% | 30.4% |
| `StockItem`, `Subscription`, `Sensor` | 13%, 36%, 16% | 45%, 69%, 26% |

Edge strings like `"a@b..c"` happen to satisfy a naive email rule, which is why Email is non-zero; neither generator makes a *realistic*
address by luck. Where AutoBogus yields more (`DateRange`, `Medication`, `Employee`, `BookingCalendar`) it is because its data never
strays: the same property that makes it unable to break rules. A higher valid yield is not a point in its favour here.

## 3. It is much faster, and it scales

The Bogus lock is real. `Randomizer.Locker` is an `internal static Lazy<object>` taken inside every `Number`/`Double`/`Bytes`/`Shuffle`
and the whole of `Faker<T>.PopulateInternal`; it guards Bogus's shared default RNG, our per-thread RNG does not need it, and it cannot be
turned off from outside. CsCheck runs one thread per core by default. Measured (draws/second, Release, same machine):

| | 1 thread | 2 | 4 | 8 |
|---|---|---|---|---|
| AutoBogus `Order` | 71,000 | 107,000 | 120,000 | 97,000 |
| `GenAuto.Of<Order>()` | 617,000 | 821,000 | 1,843,000 | 1,606,000 |
| AutoBogus `OrderLine` | 289,000 | 445,000 | 487,000 | 377,000 |
| `GenAuto.Of<OrderLine>()` | 2,104,000 | 3,666,000 | 5,924,000 | 3,353,000 |

Single-threaded the new generator is 7 to 9 times faster (most of that is Bogus and AutoBogus overhead, not the lock). AutoBogus then
**stops scaling at about 2 threads and gets slower at 8**; the new one reaches 3x. In practice this is the difference between
`iter: 10_000` being comfortable and being something you avoid.

## 4. It shrinks and replays better

- Shrinking: every leaf reports a CsCheck `Size` from an index into an ordered catalog (simplest first), a magnitude, or a collection
  count, so a failure ends on `""`, `0` or one item. The AutoBogus path reports "number of draws plus magnitude", which cannot tell
  a short word from a long one.
- Reproducibility: AutoBogus output depends on the installed Bogus dataset *version* (a Bogus update can change what a seed produces).
  The new generator draws edge values from catalogs in this repo and plausible strings from a corpus built once from a pinned Bogus seed.
  A new Bogus version can still change the corpus; that is one list to pin or check in, not every value.

## 5. What keeping it costs

- **11 packages** (`Soenneker.Utils.AutoBogus` plus 10 transitive `Soenneker.*`) for something used by one opt-in method. The package is
  a maintained fork of the abandoned `AutoBogus` (`github.com/soenneker/soenneker.utils.autobogus`); `AGENTS.md` already records choosing it for that reason.
  All eleven packages come from the same publisher family, so they move together and a single breaking release affects the whole set.
- **Reflection and caching we do not control**, and a global lock we cannot remove, in the one component every property test goes through.
- **A workaround we carry:** `IndexFaker`/`IndexVariable` reset, a custom `Random` subclass (`PcgRandom`) and `PcgRandomizer` to route
  its draws through the PCG. `PcgRandom` and the thread-local `Faker<T>` stay either way (user `Faker<T>` rules need them, and
  the corpus uses Bogus); what goes is `AutoFakerGen<T>` (about 60 lines) and the package reference.
- **Documentation cost:** every guide has to explain "plausible only, not adversarial, locks, use `raw:` for hostile data".

## 6. What AutoBogus still does better, honestly

- **Existing conventions.** A team that already configured `AutoFaker` (`Configure(c => c.WithBinder(...))`, per-type overrides, skip rules)
  keeps that investment through `GenAuto.Bogus<T>(configure)`. The new generator's equivalent is `AutoOptions.Use<T>(gen)` and
  `AddStringEdges`; it has no conventions engine and will not grow one.
- **Type coverage.** AutoBogus builds some types `AutoGen` does not: `ImmutableArray`/`ImmutableList`, `FrozenSet`, classes whose only
  constructors are non-public, `System.Tuple`, abstract types it can substitute. `AutoGen` fails with a clear message
  ("provide `AutoOptions.Use<T>(gen)`"); none of the 25 example domains hit it, but a real codebase might.
- **Not our code to maintain.** The new generator is about 900 lines (builder, edge catalog, corpus) we now own, including
  a catalog of hostile strings that should be reviewed by someone who knows the injection and Unicode corners better than I do.

## 7. Proposal

1. **Now (done):** `GenAuto.Of<T>` is the new generator; `GenAuto.Bogus<T>` is the old one. No dependency removed.
2. **Next release:** move `GenAuto.Bogus<T>` and the `Soenneker.Utils.AutoBogus` reference into `Nullean.Entities.CsCheck.AutoBogus`
   (separate, optional package). The default install drops 11 packages; users who configured `AutoFaker` add one reference.
3. **After that:** if the opt-in package has no downloads or issues in a release, delete it.
4. Before step 2: add `ImmutableArray`/`ImmutableList`/`FrozenSet`/`System.Tuple` to `AutoGen` so the "type coverage" point does not apply
   to the common cases.

The risk worth naming: this replaces a mature generator with new code. The mitigation is the test suite (determinism, edge reachability,
shrinking, every catalog category, 25 real domains) and that `GenAuto.Bogus<T>` remains for a release.

*Method: throwaway tests (not committed) over the 25 constraint sets in `examples/`, 1,000 draws per generator per set from fixed
PCG seeds; throughput in Release, 20,000 to 100,000 draws per thread after a 200-draw warm-up. Numbers vary by machine; the ratios are
what matter.*
