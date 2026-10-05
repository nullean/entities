# Nullean.Entities — contributor and agent conventions

Constraint-checked domain entities: a plain sync core (`Nullean.Entities`), a temporal/stream
layer over `IObservable<T>` (`Nullean.Entities.Reactive`), and a property-test driving layer
(`Nullean.Entities.CsCheck`) that also bundles the CsCheck/Bogus bridge for reproducible fake data.

## Build and test

```sh
./build.sh build   # clean + compile
./build.sh test    # build + run every test project (see Layout for the list)
./build.sh aot     # publish examples/Nullean.Entities.AotSmoke NativeAOT (trim/AOT warnings are errors) and run it
./build.sh apps    # run every console app under examples/Apps (`Paths.Apps`); one that exits non-zero fails the build. `test` depends on it
./build.sh pack    # build + test + pack NuGet packages
./build.sh release # pristine checkout check + pack
```

The build script is `build/scripts/Program.fs` → `Targets.fs`. It wraps `dotnet build` / `dotnet run`.
Test projects are TUnit and are run with `dotnet run`, one project at a time (`Paths.TestProjects`).

## Layout

| Path | What |
|---|---|
| `src/Nullean.Entities` | The core: `Constraint<T>`, `ConstraintSet<T>`, `Violation`, `Result<T>`, plus the `Idempotent`/`Monotonic`/`Conserves` constraint shapes and the `TimeProvider`-aware `NotInFuture`/`NotOlderThan`/`Always((x, now) => ...)`. `Apply` returns `Result<T>`; `ApplyDetailed` returns `ApplyOutcome<T>` (Applied/Noop/Failure). A type that constrains itself gets `Type.Constraints`; a companion class gets `Companion.All`. Both also get a generated `Actions` (`EntityActions<T>`: the entity's public methods returning `T` with at most one argument), reachable cross-assembly through `Companion.Instance.Actions` / `T.Actions`. No CsCheck, no System.Reactive dependency — deliberately. Also: `TimeProvider` everywhere it matters plus a public `ManualTimeProvider`; async `ApplyAsync`/`ApplyAllAsync`/`ValidateAsync`; and `Temporal<TEvent>` (pure temporal monitors over timestamped events: `Trace.Check`, `TemporalMonitor`, `MonitorAsync`) with `Verdict`, which moved here from Reactive. Reactive is unchanged and is differential-tested against it. |
| `src/Nullean.Entities.Reactive` | Lifts constraints onto `IObservable<TEvent>` streams: structural (`Never`, `NeverTwice`, `Always`, `Transitions`) and temporal (`After().Then().Within()`, `Eventually<T>` for unbounded liveness) operators, a predicate-based counterpart of each (`Never(isT1,isT2,pair)`, `AfterUntil(...)`, etc. — for event shapes where a case can't satisfy `where T : TEvent`, e.g. C# 11 unions), `MonitoredConstraintSet<TEvent>.AnyViolated`/`.Violations`, plus `ConstraintSet<T>.ToReactive()` to bridge the sync core onto a stream. |
| `src/Nullean.Entities.CsCheck` | `Script<TSut>` + `SampleWith`/`RunWith` (optionally with `Coverage<TEvent>`, which fails a sample that passed vacuously), `SampleParallelWith`, and `EntitySpec<T>` (`ConstraintSet<T>.Spec(initial, Actions)` → Exhaustive/Sample/Mermaid/Snapshot/Faults/Conform/Commutes/Idempotent over CsCheck's `Spec<S>`), plus `Gen<T>.Valid`/`ShouldSatisfy` and the AutoBogus-driven `ForAll`/`Preserves`/`Probe`/`RejectsInvalid` (`Properties.cs`): drives a SUT under CsCheck (`Gen.Operation<TSut,Unit>` + `SampleModelBased`'s replay/shrink machinery) and checks a `ReactiveConstraintSet<TEvent>` against the resulting event stream — implemented and tested, see below — plus `ScriptExtensions.Metamorphic` for confluence testing, `PcgRandom`/`PcgRandomizer`, `HubFaker<T>`, `Gen.Faker<T>()`, `GenAuto.Of<T>()` (an own, lock-free, adversarial-aware structural generator; `GenAutoBogus.Of<T>()` in the optional AutoBogus package is the AutoBogus-backed one) so seeds are reproducible and failures shrink. Also `EntityModels.cs`: other ways to declare a state machine, all producing an `EntitySpec` (declared `StateMachine<,>` via `.Spec()`/`.ConformTo()`, `FromReducer`, `EventSourced` Decide/Evolve with `SampleEvents`, `ForMutable` replay). |
| `src/Nullean.Entities.CsCheck.AutoBogus` | Optional. `GenAutoBogus.Of<T>(configure)`: the AutoBogus-backed generator (plausible data, takes Bogus's global lock). Not the default; see `docs/autobogus-decision.md`. |
| `tests/Nullean.Entities.CsCheck.AutoBogus.Tests` | Determinism, configure-once-per-thread, shrinking and thread-safety of that generator. |
| `tests/Nullean.Entities.Tests` | Sync constraint core tests, including `Idempotent`/`Monotonic`/`Conserves`. |
| `tests/Nullean.Entities.Reactive.Tests` | Reactive operator (typed and predicate-based) + `ToReactive()` bridge + `AnyViolated`/`Violations` tests. |
| `tests/Nullean.Entities.CsCheck.Tests` | `Script<TSut>`/`SampleWith`/`RunWith`/`Metamorphic` tests against a small `Counter` SUT, plus determinism tests for the Bogus-backed generators (same seed → same object). |
| `docs/spikes/docs-builder` | A spike (not in the solution) that runs these libraries against docs-builder's real `feature/erd-model` domain types: generation, ported rules compared with theirs, NativeAOT, performance. Findings in `AUTOTEST_NOTES.md` section 7. |
| `src/Nullean.Entities.FileSystem` | Constraint shapes for `IFileInfo`/`IDirectoryInfo` (System.IO.Abstractions): `Files<T>.Exist`, `StayInside`, `NotSymlink`, `NoHiddenSegments`, `HaveExtension`. Trim/AOT clean (the smoke app publishes it). |
| `src/Nullean.Entities.CsCheck.FileSystem` | `AutoOptions.UseFileSystem()`: one `MockFileSystem` world per generated value, shared by every file-system member, with missing/empty/symlinked/hidden/outside-the-root/wrong-case files. Needs `AutoOptions.NeedsScope()`/`AutoScope` from the CsCheck package. |
| `src/Nullean.Entities.Extensions.DependencyInjection` | `AddConstraints<T>()` / `ConstraintRegistry` (type-erased `TryValidate(object)`, no reflection), `OptionsBuilder<T>.ValidateConstraints()`, `ViolationErrors`. What the two framework packages below build on. Trim/AOT clean. |
| `src/Nullean.Entities.AspNetCore` | `ValidateWith<T>()` / `ValidateConstraints()` endpoint filters, `ConstraintViolationExceptionHandler` (`AddConstraintProblemDetails()`), `Result<T>`/`ApplyOutcome<T>.ToHttpResult()`, MVC `ConstraintActionFilter`. |
| `src/Nullean.Entities.Argh` | `ArghConstraints.Register<T>()`: registers a constraint set as a Nullean.Argh `ArghValidation` validator, so a violation prints as `Error: --flag: message` + the flag help rows + the usage line, exit 2, for `[AsParameters]` and global/namespace options and `TryParseArgh`. Needs Nullean.Argh 0.21 (`ArghValidation`). |
| `src/Nullean.Entities.SourceGenerators/Annotations.cs` | Part of the `[Constraint]` generator: DataAnnotations (`[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]`, `[RegularExpression]`) become generated constraints on every `[Constraints]` type, not opt-in; other validation attributes get the Info diagnostic NECG010. |
| `examples/Nullean.Entities.AotSmoke` | Published NativeAOT by `./build.sh aot`: proves the core and Reactive packages are trim/AOT clean (they build with `IsAotCompatible`; the CsCheck packages and the generator do not). |
| `examples/Nullean.Entities.Integration.Examples` | Recipes that need only the shared framework: an ASP.NET Core endpoint filter (`ValidationProblem` keyed by `Violation.Member`) and a Marten-style event-sourced aggregate. EF Core and MassTransit recipes are in `docs/integration.md` only. |
| `examples/Nullean.Entities.Examples/Docs/DocsPlatform` | The docs-eng-team domain (catalog, documentation set, site, pipeline events) as constraints, a lifecycle machine and temporal rules. See `AUTOTEST_NOTES.md` section 6. |
| `examples/Nullean.Entities.Reactive.Examples` | A ladder from simple to complex: a search box (temporal debounce with `UnlessInterruptedBy`), a retry client (exponential backoff), an order state machine — record-hierarchy events on net10.0 (`OrderStateExamples.cs`) and, on net11.0 only, the identical domain re-expressed with a real C# union and the predicate-based operators (`OrderStateExamplesNet11.cs`, `#if NET11_0_OR_GREATER`) — plus a design sketch (`StateMachineSketch.cs`) comparing four ways to attach constraints to a domain type. |
| `tests/Nullean.Entities.Examples.Tests` | Every example domain is tested with AutoTest one-liners next to its hand-written `Validate_*`/`Channel_*` tests (`Instance.Spec().Enforced().Exhaustive()`, `ForAll`, `Preserves`, `RejectsInvalid`, ...); `AutoTests/AutoTestExamples.cs` is the showcase. `AUTOTEST_NOTES.md` records what did not convert and what the conversion found in the domains. |
| `examples/Apps/*` | Eight console apps (OrderFulfilment, Banking, SupportDesk, EventSourcedCart, Inventory, DataAudit, OrgChart, DocsSiteBuild), each an Exe with a narrated `Program.cs` that exits non-zero when a scenario stops behaving, a service wired through `Microsoft.Extensions.DependencyInjection` (the libraries do not depend on it), and a README of what to copy. `examples/Apps/Shared/Narrator.cs` is linked into each. New app: add the folder, a `ProjectReference` in `tests/Nullean.Entities.Apps.Tests`, the slnx entry and `Paths.Apps`. |
| `tests/Nullean.Entities.Apps.Tests` | Tests the apps' services the way a user would: by example, against the declared machine (`ConformsTo`), by the rules on the state they expose (`SampleState`), as properties, and with `Probe`/`Laws` on generated data. |
| `docs/first-use-review.md` | A review of the library from six points of view (newcomer, DDD expert, DBA, functional programmer, property-test expert, verification student): what each expected, what was built, what is open. |
| `build/scripts` | F# Bullseye build runner. |
| `build/keys` | Nullean strong-name key — never regenerate, it breaks strong-naming. |

## Multi-targeting: net10.0 and net11.0

Every `src`/`tests`/`examples` project multi-targets `net10.0;net11.0` (set once in the root
`Directory.Build.props`). `src/Nullean.Entities.SourceGenerators` is the one exception — it stays
pinned to `netstandard2.0` (a Roslyn analyzer runs inside the host compiler process, not the
consuming project's TFM) by clearing the inherited `TargetFrameworks` and setting a single
`TargetFramework` explicitly; clearing it first matters, since a non-empty inherited
`TargetFrameworks` wins over a project-local `TargetFramework` otherwise.

`global.json` pins the .NET 11 preview SDK (`allowPrerelease: true`) rather than .NET 10, because a
newer SDK can always build older TFMs but the reverse does not hold for an unreleased one. Expect
`NETSDK1057` ("you are using a preview version of .NET") on every build — that is expected, not a
problem to silence.

C# 11's discriminated unions (`readonly union Name(Case1, Case2, ...);`) only parse under
`LangVersion=preview` in the installed `11.0.100-preview.7` SDK — `LangVersion=latest` resolves to
the stable C# version that ships with it and rejects `union` outright with CS1001. `Directory.Build.props`
sets `LangVersion=preview` conditionally, scoped to `'$(TargetFramework)' == 'net11.0'` only, so
net10.0 stays on `latest` and does not silently pick up other preview syntax never exercised
against it.

**Union case types are not subtypes of the union.** `readonly union OrderStateEventUnion(Placed,
Confirmed, ...)` compiles to a sealed type; `Placed` etc. are ordinary top-level sibling types with
compiler-synthesized pattern-matching support, not classes deriving from the union — confirmed by
hand: `where T : OrderStateEventUnion` is rejected outright with CS0701 ("not a valid constraint"),
so no case type can ever satisfy it either. Every operator in `Constraints<TEvent>` that reads
`where T1 : TEvent` (`Never<T1,T2>`, `NeverTwice<T>`, `Always<T>`, `After<TTrigger>`, `Eventually<T>`)
is therefore unusable against a union — hence the predicate-based counterparts (`Never(isT1,isT2,pair)`,
`NeverTwice(isRelevant,key)`, `Always(isRelevant,predicate)`, `Eventually(scope,from,isTarget)`,
`AfterUntil(isTrigger,isNext,window,isInterrupt)`), which classify via `Func<TEvent,bool>`/pattern
matching instead of a type parameter and work identically whether `TEvent` is a record hierarchy or
a union. `Transitions(Func<TEvent,TEvent,bool>)` already took no type parameter and needed no
change. `OrderStateExamplesNet11.cs` is the worked example: the same domain, spec, and `Script<TSut>`
run as `OrderStateExamples.cs`, expressed with a union and the predicate-based operators, with a
`switch` over all four cases and no discard arm proving compiler-enforced exhaustiveness (delete a
case and rebuild — CS8509, a hard failure under `TreatWarningsAsErrors`).

A union's case list also does not auto-nest its cases the way a record hierarchy's nested records
do — `readonly union Name(Case1, Case2);` names pre-existing sibling types, it does not declare and
nest them, so there is no `Name.Case1`. Group cases in a plain container class instead
(`OrderStateEventCases.Placed`) for the closest equivalent to `OrderStateEvent.Placed` dotted access.

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
- **`Script<TSut>`, `ScriptExtensions.SampleWith`/`RunWith`/`Metamorphic` are implemented and
  tested.** `Script<TSut>` builds a `Gen.OneOf` over named/generated steps and drives CsCheck's own
  `SampleModelBased` replay/shrink machinery against a fresh SUT (and a fresh `HistoricalScheduler`)
  per generated run; `SampleWith` attaches a `ReactiveConstraintSet<TEvent>` to the resulting event
  stream and uses `MonitoredConstraintSet.AnyViolated` as CsCheck's own `equal` check, so a
  violation *is* a CsCheck failure with a real reproducible seed. `RunWith` is the same thing as an
  `IObservable<EntityEvent>` for a caller that wants the play-by-play (`IterationStarted`,
  `ConstraintResolved`, `SmallerFailure`, `Completed`) instead of an exception. See
  `tests/Nullean.Entities.CsCheck.Tests/ScriptTests.cs` for the full behaviour, including the
  virtual-time semantics: `AdvanceBy` only runs before each step (its own pre-delay), never after
  the last one, so an obligation still pending when a run ends stays `Pending`, not `Violated`.

## Bogus internals relied on

- `Randomizer.localSeed` is `protected`. A subclass can assign it in its constructor.
- `Faker.Random` is a settable property.
- `Faker<T>.FakerHub` is `protected internal`. Access it through `sealed class HubFaker<T> : Faker<T>`.
- Bogus takes a **global lock** on every draw. Multi-threaded CsCheck runs serialise through it —
  document this, do not fight it.
- Use **Soenneker.Utils.AutoBogus** (not the abandoned nickdodd79/AutoBogus). Set
  `autoFaker.Faker.Random = new PcgRandomizer(pcgRandom)` per-generate, not once at construction.

## Spiked and dropped: ASP.NET Core built-in validation resolver

An `IValidatableInfoResolver` (Microsoft.Extensions.Validation) would let `services.AddValidation()` validate registered constraint types with no per-endpoint filter. Not shipped:
on net10.0 the whole surface is flagged evaluation-only (ASP0029, "subject to change or removal"), and net11.0 already changed it (`IValidatableTypeInfo` / `IValidatableParameterInfo` out
parameters replace `IValidatableInfo`, so one source file cannot serve both TFMs). Revisit once it is stable; until then `ValidateWith`/`ValidateConstraints()` cover the minimal-API case.

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
   and why. — Open.
2. **Early exit cost.** Once running size passes `min`, abort generation. Benchmark vs always
   generating and reporting size. Record the result. — Open.
3. **Global lock throughput.** Measured (Release, draws/second): AutoBogus-backed `Order` 71k on 1 thread, 120k on 4, 97k on 8;
   the CsCheck-native `GenAuto.Of<Order>()` 617k, 1.84M, 1.61M. Bogus's `Randomizer.Locker` is an `internal static` taken inside every
   draw and cannot be disabled, so a Bogus-backed generator stops scaling at about 2 threads. Answer: `GenAuto.Of<T>` no longer draws
   from Bogus at all (a one-time corpus, picked by index); only user `Faker<T>` rules still take the lock. Details and the case for dropping
   the AutoBogus dependency: `docs/autobogus-decision.md`. — Answered.
4. **Existing fakers.** `faker.ToGen()` on an existing `Faker<T>` needs `FakerHub`, which is
   `protected internal`. Decision: support it via reflection, or support only `Gen.Faker<T>(configure)`?
   — Open; `Gen.Faker<T>(configure)` (fresh `Faker<T>` per call, not adapting an existing instance)
   is what shipped.
5. **Reproducibility.** A test asserts that `CsCheck_Seed` replays to the identical object. Do
   not ship without this. — Done: `tests/Nullean.Entities.CsCheck.Tests/DeterminismTests.cs`.
6. **`Script<TSut>` implementation.** Driving a SUT from a generated action sequence, replaying a
   shrunk seed, and producing `EntityEvent.SmallerFailure` traces are the next things to build in
   `Nullean.Entities.CsCheck`. — Done, see the non-negotiable above and `ScriptTests.cs`.
