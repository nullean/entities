# Nullean.Entities

Constraint-checked domain entities for .NET: name a rule once, check it synchronously against a
value or continuously against an event stream.

A `Constraint<T>` is a named rule (`Func<T, IEnumerable<Violation>>`) that lives anywhere — a static
field on the type it constrains, a local variable, a constructor parameter. A `ConstraintSet<T>` is
an ordered collection of them, with `Validate`, `Apply`, and `ApplyOrThrow` for safe, synchronous
mutation. That is the whole idea, and it costs a consumer nothing beyond this package: no CsCheck, no
System.Reactive.

## Packages

| Package | What |
|---|---|
| [`Nullean.Entities`](src/Nullean.Entities) | The core: `Constraint<T>`, `ConstraintSet<T>`, `Violation`, `Result<T>`. No dependencies beyond the BCL. |
| [`Nullean.Entities.Reactive`](src/Nullean.Entities.Reactive) | Lifts constraints onto `IObservable<TEvent>` streams: structural operators (`Never`, `NeverTwice`, `Transitions`) and temporal operators (`After().Then().Within()`) that only make sense against a stream, plus `ConstraintSet<T>.ToReactive()` to attach the sync core to one. |
| [`Nullean.Entities.CsCheck`](src/Nullean.Entities.CsCheck) | Drives a `Script<TSut>` under [CsCheck](https://github.com/AnthonyLloyd/CsCheck) and checks a `ReactiveConstraintSet<TEvent>` against the resulting event stream. Declared against its final API shape; still unimplemented — see `AGENTS.md`. |
| [`Nullean.Entities.Bogus`](src/Nullean.Entities.Bogus) | Unrelated to the other three: routes [Bogus](https://github.com/bchavez/Bogus)/[AutoBogus](https://github.com/Soenneker/Soenneker.Utils.AutoBogus) draws through CsCheck's `PCG`, so seeds stay reproducible and failing cases shrink through CsCheck's size mechanism. |

## Why a sync core and a reactive layer

Most validation libraries either check a value once or watch a stream forever — never both from the
same rule declaration. `Nullean.Entities` is deliberately just the synchronous half: a constraint set
that validates a value or an `[old, new]` transition, with no stream, no scheduler, no threading model
to reason about. `Nullean.Entities.Reactive` adds what only makes sense once you have a stream —
"never twice", "after X, then Y within 15 minutes" — and `ToReactive()` lifts a plain `ConstraintSet<T>`
onto one when a synchronous rule needs to run continuously instead of at a single call site.

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Reactive
dotnet add package Nullean.Entities.CsCheck
dotnet add package Nullean.Entities.Bogus
```

## Building from source

Requires the .NET 10 SDK (pinned in `global.json`).

```sh
git clone https://github.com/nullean/entities.git nullean-entities
cd nullean-entities
./build.sh build
./build.sh test
```

See [AGENTS.md](AGENTS.md) for the full build target list, package layout, and design conventions.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
