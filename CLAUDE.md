# Nullean.Entities

See [AGENTS.md](AGENTS.md) for build commands, package layout, non-negotiables, and the spike
questions that must be answered before 1.0. It is the source of truth; this file only adds
orientation.

## The one-paragraph version

Four packages, one idea: a `Constraint<T>` is a named, syntax-level rule with a `Check` function,
and a `ConstraintSet<T>` is an ordered collection of them. `Nullean.Entities` is that idea alone —
no CsCheck, no System.Reactive, so it costs a consumer nothing to hold constraints on a domain type.
`Nullean.Entities.Reactive` lifts constraints onto `IObservable<TEvent>` streams, adding temporal and
structural operators (`Never`, `NeverTwice`, `After().Then().Within()`) that only make sense against
a stream, plus `ConstraintSet<T>.ToReactive()` to attach the sync core to one. `Nullean.Entities.CsCheck`
drives a `Script<TSut>` under CsCheck and checks a `ReactiveConstraintSet<TEvent>` against the
resulting event stream — still unimplemented scaffolding, see below. `Nullean.Entities.Bogus` is
unrelated to the other three: it routes Bogus/AutoBogus draws through CsCheck's `PCG` so seeds are
reproducible and failures shrink.

## Design decisions worth not re-litigating

- **`Nullean.Entities` has zero CsCheck/System.Reactive dependency, on purpose.** A domain type that
  wants to declare constraints should not have to pull in a property-testing library or Rx to do it.
  The Reactive and CsCheck packages layer on top; they never move logic back down into the core.
- **`ConstraintSet<T>.ToReactive()` is the only bridge between the sync core and the stream layer.**
  It does not gain the temporal/structural operators declared directly in `Constraints<TEvent>` —
  those still need to be authored in Reactive terms, because "after X, then Y, within a time window"
  has no sync equivalent to lift from.
- **`Script<TSut>`, `SampleWith`/`RunWith` are intentionally unimplemented.** Every overload throws
  `NotImplementedException`. They exist so the examples compile against the final API shape and
  demonstrate the intended call pattern; nothing downstream depends on them executing yet.
- **`PcgRandom : System.Random`.** Bogus has no injection point for a custom RNG except
  `Randomizer.localSeed` (protected). Subclassing is the only approach that does not require
  patching Bogus.
- **`ThreadLocal<Faker<T>>`.** Bogus takes a process-wide lock per draw. A faker per thread is the
  smallest unit that avoids contending on the `ThreadLocal` itself, given the Bogus lock is
  unavoidable either way.

## Current state

Merged from two prior repos, `cscheck-bogus` and `cscheck-reactive`, kept as reference copies and
not deleted. Formatting is enforced by [curb](https://curb.nullean.net) via the MSBuild integration
and a Husky.Net pre-commit hook, the same way [nullean/curb](https://github.com/nullean/curb) formats
itself — see the Code Style section in `AGENTS.md`.

Still open: `Script<TSut>` itself (driving a SUT from a generated action sequence, replaying a shrunk
seed, producing `EntityEvent.SmallerFailure` traces), and the spike questions in `AGENTS.md` that gate
a 1.0.
