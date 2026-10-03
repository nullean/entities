# Nullean.Entities

See [AGENTS.md](AGENTS.md) for build commands, package layout, non-negotiables, and the spike
questions that must be answered before 1.0. It is the source of truth; this file only adds
orientation.

## The one-paragraph version

Three packages (plus an optional fourth), one idea: a `Constraint<T>` is a named, syntax-level rule with a `Check` function,
and a `ConstraintSet<T>` is an ordered collection of them. `Nullean.Entities` is that idea alone —
no CsCheck, no System.Reactive, so it costs a consumer nothing to hold constraints on a domain type
— plus the `Idempotent`/`Monotonic`/`Conserves` shapes. `Nullean.Entities.Reactive` lifts constraints
onto `IObservable<TEvent>` streams, adding temporal and structural operators (`Never`, `NeverTwice`,
`Always`, `Transitions`, `After().Then().Within()`, `Eventually<T>` for unbounded liveness) that only
make sense against a stream, a predicate-based counterpart of each for event shapes that cannot
satisfy `where T : TEvent` (C# 11 unions — see `AGENTS.md`), `MonitoredConstraintSet<TEvent>.AnyViolated`,
plus `ConstraintSet<T>.ToReactive()` to attach the sync core to a stream. `Nullean.Entities.CsCheck`
drives a `Script<TSut>` under CsCheck and checks a `ReactiveConstraintSet<TEvent>` against the
resulting event stream via `SampleWith`/`RunWith` (`MonitoredConstraintSet.AnyViolated` is CsCheck's
own `equal` check, so a violation is a real, reproducible CsCheck failure), plus `Metamorphic` for
confluence testing and the Bogus/AutoBogus bridge: generators that route their draws through
CsCheck's `PCG` so seeds are reproducible and failures shrink.

## Design decisions worth not re-litigating

- **`Nullean.Entities` has zero CsCheck/System.Reactive dependency, on purpose.** A domain type that
  wants to declare constraints should not have to pull in a property-testing library or Rx to do it.
  The Reactive and CsCheck packages layer on top; they never move logic back down into the core.
- **`ConstraintSet<T>.ToReactive()` is the only bridge between the sync core and the stream layer.**
  It does not gain the temporal/structural operators declared directly in `Constraints<TEvent>` —
  those still need to be authored in Reactive terms, because "after X, then Y, within a time window"
  has no sync equivalent to lift from.
- **`Script<TSut>`, `SampleWith`/`RunWith`/`Metamorphic` are implemented and tested**, backed by
  CsCheck's own `Gen.OneOf` + ranged `.Array[min,max]` step selection and `SampleModelBased`'s
  replay/shrink machinery — see the non-negotiable in `AGENTS.md`.
- **A union case type is not a subtype of the union.** `readonly union Name(Case1, Case2);` compiles
  to a sealed type; `Case1`/`Case2` are ordinary sibling types with compiler-only pattern-matching
  support, so `where T : TEvent` (used throughout `Constraints<TEvent>`'s typed operators) can never
  be satisfied by one — confirmed with CS0701 against the union type itself. The predicate-based
  operators exist because of this, not as a style preference — see `AGENTS.md`'s multi-targeting
  section for the full finding and `OrderStateExamplesNet11.cs` for the worked example.
- **`PcgRandom : System.Random`.** Bogus has no injection point for a custom RNG except
  `Randomizer.localSeed` (protected). Subclassing is the only approach that does not require
  patching Bogus.
- **`ThreadLocal<Faker<T>>`.** Bogus takes a process-wide lock per draw. A faker per thread is the
  smallest unit that avoids contending on the `ThreadLocal` itself, given the Bogus lock is
  unavoidable either way.

## Current state

`Nullean.Entities.CsCheck.AutoBogus` is the optional fourth: an AutoBogus-backed generator (`GenAutoBogus.Of<T>`) for teams that already configure `AutoFaker`; the default `GenAuto.Of<T>` does not use AutoBogus.

Merged from two prior repos, `cscheck-bogus` and `cscheck-reactive`, kept as reference copies and
not deleted. Formatting is enforced by [curb](https://curb.nullean.net) via the MSBuild integration
and a Husky.Net pre-commit hook, the same way [nullean/curb](https://github.com/nullean/curb) formats
itself — see the Code Style section in `AGENTS.md`.

Every project multi-targets `net10.0;net11.0` (the source generator stays on `netstandard2.0`); see
`AGENTS.md`'s multi-targeting section for the `LangVersion`/union-constraint findings behind it.
`Script<TSut>`, `SampleWith`/`RunWith`/`Metamorphic`, and the new constraint shapes (`Idempotent`,
`Monotonic`, `Conserves`, `Eventually<T>`) are implemented and tested on both TFMs — an
`EntitySpec<T>` (`ConstraintSet<T>.Spec(initial, Actions)`) turns an entity's constraints plus its
generated `Actions` into one-line `Exhaustive`/`Sample`/`Mermaid`/`Faults`/`Conform`/`Commutes` tests;
`tests/Nullean.Entities.Examples.Tests` runs them against every example domain (see `AUTOTEST_NOTES.md` for what did not convert).

The core and Reactive packages are trim/AOT clean and verified by publishing `examples/Nullean.Entities.AotSmoke` NativeAOT (`./build.sh aot`); the CsCheck packages and the generator are
not, by design (reflection). The `[Constraints]` generator also turns DataAnnotations into constraints (no opt-in) and supports nested types; generic types are still refused (NECG004).
`Violation.Member`, `ConstraintViolationException`, `Unique`/`References`/`Acyclic`/`NoChains`, `Laws()`, and AutoGen's value-object, union, abstract-type, DataAnnotations and foreign-key support are
documented in the package READMEs; `docs/integration.md` covers real services and `AUTOTEST_NOTES.md` section 6 the docs-platform example.

Still open: the spike questions in `AGENTS.md` that gate a 1.0 (shrinking quality, early-exit cost,
global-lock throughput, `faker.ToGen()` support).
