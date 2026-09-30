# Nullean.Entities — contributor and agent conventions

Constraint-checked domain entities: a plain sync core (`Nullean.Entities`), a temporal/stream
layer over `IObservable<T>` (`Nullean.Entities.Reactive`), a property-test driving layer
(`Nullean.Entities.CsCheck`), and a CsCheck/Bogus bridge for reproducible fake data
(`Nullean.Entities.Bogus`).

## Build and test

```sh
./build.sh build   # clean + compile
./build.sh test    # build + run every test project (see Layout for the list)
./build.sh pack    # build + test + pack NuGet packages
./build.sh release # pristine checkout check + pack
```

The build script is `build/scripts/Program.fs` → `Targets.fs`. It wraps `dotnet build` / `dotnet run`.
Test projects are TUnit and are run with `dotnet run`, one project at a time (`Paths.TestProjects`).

## Layout

| Path | What |
|---|---|
| `src/Nullean.Entities` | The core: `Constraint<T>`, `ConstraintSet<T>`, `Violation`, `Result<T>`. No CsCheck, no System.Reactive dependency — deliberately. |
| `src/Nullean.Entities.Reactive` | Lifts constraints onto `IObservable<TEvent>` streams: structural (`Never`, `NeverTwice`, `Transitions`) and temporal (`After().Then().Within()`) operators, plus `ConstraintSet<T>.ToReactive()` to bridge the sync core onto a stream. |
| `src/Nullean.Entities.CsCheck` | `Script<TSut>` + `SampleWith`/`RunWith`: drives a SUT under CsCheck and checks a `ReactiveConstraintSet<TEvent>` against the resulting event stream. **Intentionally unimplemented** — see below. |
| `src/Nullean.Entities.Bogus` | `PcgRandom`/`PcgRandomizer`, `HubFaker<T>`, `Gen.Faker<T>()`, `GenAuto.Of<T>()` — routes Bogus/AutoBogus draws through CsCheck's `PCG` so seeds are reproducible and failures shrink. Standalone: no `ProjectReference` to `Nullean.Entities`. |
| `tests/Nullean.Entities.Tests` | Sync constraint core tests. |
| `tests/Nullean.Entities.Reactive.Tests` | Reactive operator + `ToReactive()` bridge tests. |
| `tests/Nullean.Entities.Bogus.Tests` | Determinism tests (same seed → same object). |
| `examples/Nullean.Entities.Reactive.Examples` | Three usage styles (fluent, constraints-class, constraints-as-event-source) over a search box, a retry client, and an order state machine, plus a design sketch (`StateMachineSketch.cs`) comparing four ways to attach constraints to a domain type. |
| `examples/Nullean.Entities.Bogus.Examples` | Bogus/AutoBogus usage. |
| `build/scripts` | F# Bullseye build runner. |
| `build/keys` | Nullean strong-name key — never regenerate, it breaks strong-naming. |

## Non-negotiables

- **`Nullean.Entities` has zero CsCheck/System.Reactive dependency.** Constraints must be
  expressible and checkable without either. The Reactive and CsCheck packages layer on top; they
  never move logic back down into the core.
- **`PcgRandom` must override every virtual `System.Random` method**: `Next()`, `Next(int)`,
  `Next(int,int)`, `NextInt64` ×3, `NextDouble`, `NextSingle`, `NextBytes(byte[])`,
  `NextBytes(Span<byte>)`, and `Sample()`. Missing one breaks reproducibility silently.
- **`FakerGen<T>` uses a `ThreadLocal`** so one `Faker<T>` per thread avoids the global Bogus lock
  during multi-threaded CsCheck runs. Do not change this to a single shared instance.
- **`Size` is derived from draw count and magnitude.** Do not record `DateTime`, `Guid`, or any
  draw that is always the same regardless of value (like a flag). Record only draws that represent
  domain data.
- **`Script<TSut>`, `ScriptExtensions.SampleWith`/`RunWith` are intentionally unimplemented** —
  every overload throws `NotImplementedException`. They are declared against the final API shape
  so the examples compile and demonstrate the intended call pattern, but nothing downstream
  depends on them executing yet. Do not "helpfully" implement them without checking first; this
  is checkpoint-A scaffolding, not a gap to be filled silently.

## Bogus internals relied on

- `Randomizer.localSeed` is `protected`. A subclass can assign it in its constructor.
- `Faker.Random` is a settable property.
- `Faker<T>.FakerHub` is `protected internal`. Access it through `sealed class HubFaker<T> : Faker<T>`.
- Bogus takes a **global lock** on every draw. Multi-threaded CsCheck runs serialise through it —
  document this, do not fight it.
- Use **Soenneker.Utils.AutoBogus** (not the abandoned nickdodd79/AutoBogus). Set
  `autoFaker.Faker.Random = new PcgRandomizer(pcgRandom)` per-generate, not once at construction.

## Suppressing warnings

**Never add a file-wide `#pragma warning disable`.** Either:

- Move it to a `PropertyGroup`'s `<NoWarn>` in `Directory.Build.props` (or the project's own
  `.csproj`) if it applies to a whole class of files (e.g. `CS1591` — no doc comments yet in
  `src/`), or
- Wrap only the specific lines with a `#pragma warning disable X` / `#pragma warning restore X`
  pair (e.g. `IDE0060` around the no-op stub methods in `Script.cs`, `CA1000` around a static
  member on a generic type in `ReactiveConstraints.cs`).

Always raise it in review rather than deciding unilaterally — a suppression is a decision about
what the build is allowed to stop catching.

## Code Style

Mechanical formatting is fully enforced by `.editorconfig` and [curb](https://curb.nullean.net)
via the MSBuild integration (auto-rewrites on `dotnet build` in Debug mode) and the Husky.Net
pre-commit hook (`dotnet tool run curb -- check .`). To fix manually: `dotnet tool run curb -- format .`.
Never use `--no-verify`.

`examples/` is held to the same `TreatWarningsAsErrors=true` bar as `src/` and `tests/` — example
code is real code.

## Signing

CsCheck 4.9.1 is not strong-named. CS8002 (unsigned reference from signed assembly) is a warning,
not an error by default, and `TreatWarningsAsErrors` in this repo does not escalate it because
`IsAotCompatible=false` relaxes several warning categories. Verify on first build and document
here if that changes.

## Spike questions (answer before 1.0; answers go here)

1. **Shrinking quality.** A property failing only when Lines > 3 and a SKU contains `7` should
   shrink to ~4 lines. Compare single-node `Size` vs chained-per-RuleFor `Size`. Record the winner
   and why.
2. **Early exit cost.** Once running size passes `min`, abort generation. Benchmark vs always
   generating and reporting size. Record the result.
3. **Global lock throughput.** Compare `threads: 1` vs `threads: N` with a `FakerGen`. If
   contention dominates, document it and recommend `threads: 1` in the README.
4. **Existing fakers.** `faker.ToGen()` on an existing `Faker<T>` needs `FakerHub`, which is
   `protected internal`. Decision: support it via reflection, or support only `Gen.Faker<T>(configure)`?
5. **Reproducibility.** A test asserts that `CsCheck_Seed` replays to the identical object. Do
   not ship without this.
6. **`Script<TSut>` implementation.** Driving a SUT from a generated action sequence, replaying a
   shrunk seed, and producing `EntityEvent.SmallerFailure` traces are the next things to build in
   `Nullean.Entities.CsCheck`.
