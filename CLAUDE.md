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

`Nullean.Entities.FileSystem` (file rules over System.IO.Abstractions) and `Nullean.Entities.CsCheck.FileSystem` (generated MockFileSystem worlds) are two more optional packages.
The core, Reactive and FileSystem packages are trim/AOT clean and verified by publishing `examples/Nullean.Entities.AotSmoke` NativeAOT (`./build.sh aot`); the CsCheck packages and the generator are
not, by design (reflection). The `[Constraints]` generator also turns DataAnnotations into constraints (no opt-in) and supports nested and generic types (a generic companion names its type with a field or `[Constraints]`; C# does not allow a type parameter in an attribute argument, so `[Constraints<T>]` takes a closed type).
`Violation.Member`, `ConstraintViolationException`, `Unique`/`References`/`Acyclic`/`NoChains`, `Laws()`, and AutoGen's value-object, union, abstract-type, DataAnnotations and foreign-key support are
documented in the package READMEs; `docs/integration.md` covers real services and `AUTOTEST_NOTES.md` section 6 the docs-platform example.

Three integration packages sit on the core and know nothing of CsCheck: `Nullean.Entities.Extensions.DependencyInjection` (`AddConstraints<T>()`, `ValidateConstraints` for options, `IConstraintRegistry` that validates a registered instance by its exact type, AOT-safe),
`Nullean.Entities.AspNetCore` (a `ConstraintViolationException` handler and results that turn violations into ProblemDetails keyed by `Violation.Member`) and `Nullean.Entities.Argh` (validates the option objects an Argh command receives).

There are two state machines, and they are complementary: `StateMachine<TState, TTrigger>` (states are values, moved by named triggers; Mermaid in and out, `ConformTo`, `Spec()`; trim/AOT clean) and
`StateMachine<TState>` (states are case types of a union or record hierarchy, with guards over the case values, structural checks NESM001-004 at definition time, `Walks()` and `RealizedWalks()`; it finds union cases by reflection,
so it is marked `[RequiresUnreferencedCode]`). `RealizedWalks(mode:, configure:)` draws its case values with AutoGen.

`StateMachine<TState>` also runs: an edge can carry a factory (`To<Placed>(d => new Placed(0))`, `On<int>("AddLines").To<Placed>((p, n) => ...)`), so `Fire`, `Transition`, `Permitted` and `machine.For<TEntity>(get, with)` (a `MachineBinding` that fires on an aggregate and checks a constraint set) work; an edge without a factory is declared only.
`ConstraintSet.Validated`/`Ensure` and `Result.Map/Bind/Combine` are the construction story (check in a factory, not a constructor); `Script.SampleState`, `machine.ConformsTo` and `ConformToService` test a service with no Rx. `examples/Apps` has eight runnable console apps
(`./build.sh apps`, tested in `tests/Nullean.Entities.Apps.Tests`) and `docs/first-use-review.md` records what building them found. `Apply` runs no rule for a result equal to its input: use `ApplyDetailed` where "nothing happened" is an error.

All the spike questions in `AGENTS.md` are answered (shrinking quality and early-exit cost with `docs/spikes/shrinking`), and what they found is fixed in `GenAuto`: collection counts can reach every
value up to `MaxCollection`, sizes put structure first, collections that plan at least `AutoOptions.EarlyExitFrom` items stop early while shrinking, and `Probe` reports how large and deep the values it
drew were (`ProbeReport.Reach`).
