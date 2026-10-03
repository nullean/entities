# AutoTest conversion notes

Written while converting every example and domain test to the AutoTest layer
(`EntitySpec`, `ForAll`, `Preserves`, `Probe`, `RejectsInvalid`, `Coverage`). Three kinds of finding:

1. **Domain findings**: the AutoTests found real gaps in the example domains.
2. **Tooling gaps**: things that did not convert cleanly, with an idea for each and a rough size.
3. **Left as hand-written**, deliberately.

Nothing here is a blocker; the suite is green (`./build.sh test`).

## 1. Domain findings (the examples, not the library)

| Domain | Finding | Found by |
|---|---|---|
| `DateRange` | `Extend(end)` accepts an end before the start. `StartBeforeEnd` only rejects it at `Apply`. | raw `Exhaustive`/`Preserves` |
| `StockItem` | `Reserve(-n)` is accepted by the method (found as `Reserve(-2104519966)`); only `ReservedNeverNegative` stops it at `Apply`. | raw `Preserves` |
| `BankAccount` | `Withdraw` ignores the overdraft limit. **Also** `DepositIsMonotonic` sits in the general set (unlike `Order`/`Patient`, which step-scope theirs with `[ConstraintIgnore]`), so through `Apply` every `Withdraw` is rejected. A withdrawal can never succeed. | raw `Exhaustive`; enforced run |
| `ShoppingCart` | Same pattern: `AddLineGrowsTotal` is in the general set, so `Clear()` on a non-empty cart is rejected by `Apply`. The existing test only covers clearing an *empty* cart (a Noop), which hides it. | enforced run |
| `Subscription` | `AddSeats(-n)` is accepted by the method; only `SeatCountPositive` stops it at `Apply`. | raw `Preserves` |
| `SupportTicket` | **`AwaitingCustomer` is unreachable**: no action produces it, so the legal edges `InProgress -> AwaitingCustomer`, `AwaitingCustomer -> InProgress/Resolved` are never taken. | `Exhaustive` coverage |
| `Person` (HR) | **`OnLeave` is unreachable**: no action puts someone on leave (`Active -> OnLeave`, `OnLeave -> Active/Terminated` never taken). A terminated person can end up with a manager again (shortest counterexample in the raw run). | `Exhaustive` coverage; raw run |
| `OrgChart` | `AddPerson` accepts a duplicate id, and `NoCycles` then **throws** `ArgumentException` (`ToDictionary`) instead of reporting a violation. Constraints should not throw on bad data. | enforced run |
| `DeploymentPipeline` | Actions are unguarded (`Start()` from `Cancelled`). `Complete/Fail/Cancel` stamp `DateTimeOffset.UtcNow` inside the entity, so every call yields a "new" state. | raw run; state space never closed |
| `PatientRecord` | `Discontinue` reads `DateTime.UtcNow` inside the entity, and `AdmittedPatientsHaveRecentVitals` reads the clock too, so results depend on when the test runs. | code reading |
| `Sensor` | `ReadingsWithinThresholds` reports `Severity.Warning`, but `Apply` and every AutoTest treat a warning as a failure. Severity is ignored everywhere. | enforced run |
| `Nullean.Entities` | **Fixed during this work:** `Transitions(selector).Allow(...)` rejected mutations that left the selected state alone (`AddLine` on a `Pending` order). | `Exhaustive` on `Order` |
| `Reactive.Examples` | `SearchBoxExamples.SettlesToQueryConstraintHoldsAfterDebounce` **fails on the original commit** too (expects `Holds`, gets `Pending`). Cause found while building the core monitors: `AfterUntil` and `After().Then().Within()` filter out `Holds` (`.Where(v => v.IsViolated)`), so they only ever report `Pending` or `Violated`; the test asserts something the operator never does. Either the operator should report `Holds` for an answered obligation, or the test should assert `Pending`. `./build.sh test` does not run the example projects' own tests, so CI never sees it. Not touched here. | running the example project |

Unfixed and worth a decision: the two "constraint in the wrong set" cases (`BankAccount`, `ShoppingCart`) are
one-line fixes (`[ConstraintIgnore]` plus a step set, as `Order` does) but change what the examples teach.

## 2. Tooling gaps and ideas

### AutoBogus cannot build values under tight constraints
`EmailAddress` (needs `@` and `.`), `JournalEntry` (must balance), `VitalSigns` (plausibility ranges),
`PatientRecord`, `OrgChart` (referential integrity). Zero-config fails with a clear "no valid X in 400
attempts, rejected by A, B" message; each test supplies a start (`Spec(start)`) or a Bogus rule instead.

- *Idea A, small:* an optional `static Gen<T> Generator` convention on the companion class, picked up by
  `Spec()`, `ForAll` and `Valid` before falling back to `GenAuto`.
- *Idea B, medium:* `GenAuto.Of<T>(configure)` registry keyed by type (`Email -> Internet.Email()`), set once per test assembly.
- *Idea C, large:* constraint-guided repair (shrink an invalid AutoBogus value toward validity). Probably not worth it.

### Generated data is now hostile by default (was: plausible, not adversarial)
`GenAuto.Of<T>` mixes in edge values (see the CsCheck README table), so Money and Temperature no longer need a hand-made `raw:`
generator. Not done yet:
- **Foreign keys between different collections** (`Order.CustomerId` into a separate customer list). Within one collection they are generated: valid forests,
  and a reference to nothing, to itself or round a cycle (`Person.ManagerId`). Duplicate ids and `Start/End` pairs are generated too.
- **Types AutoGen cannot build:** interfaces, abstract types, classes with no public constructor. (`ImmutableArray`, `FrozenSet`, `System.Tuple` and friends are built now.)
- **Overlap and rest-period rules over time slots** (`RoomBooking`, `ShiftRoster`) are still never violated (structured, not leaf, relations).

### Argument domains
Now two plausible values plus the edge values that matter for the type (`""`/`" "`, `0`/`-1`, `NaN`, earliest/latest date). Open: a numeric
domain still does not know "just past the limit" (it cannot see the rule), which `Boundaries()` reports per property.

### Numbers that only grow need a `Key` or `Boundary`
`BankAccount` and `Subscription` do not close without one (`.Key(a => (a.Status, a.Balance))`, a boundary).
`Exhaustive` says so rather than passing, but the user has to know to do it.
- *Idea, medium:* interval abstraction for numeric properties (negative / zero / positive / over-threshold).

### Nondeterministic actions break state-space closing
`UtcNow` inside an entity (Pipeline, Patient) makes every step a new state. Needs a `.Key(...)` that ignores the timestamp.
- *Idea, small:* `Exhaustive` runs each action twice from the same state and reports "action X is nondeterministic".
- *Idea (domain side):* entities take a `TimeProvider`/timestamp argument instead of reading the clock.

### Static functions and multi-argument methods are not actions
`Transfers.Move(pair, amount)`, `MoneyOps.Split`, `OrderTransfers.TransferLine(from, to, sku)` are static or take
two arguments, so no generated `Actions`; `TransferTests` declares the action by hand (`.Action("Move", [...], Transfers.Move)`).
The pair-valued sets (`Money`, `Transfer`, `StockMovement`, `OrderTransfer`) otherwise only get `RejectsInvalid`.
- *Idea, medium:* generate actions from static methods whose first parameter is `T` on a sibling `*Ops` class
  (`[ConstraintsFor(typeof(Account))]`), and support tuple arguments (`(A, B)` domains from a product).

### Severity is ignored
See `Sensor`. Either `Apply`/`Validate` should treat `Warning`/`Hint` as non-blocking, or AutoTest needs an option.
This is a core-library semantics question, not a testing one.

### Enforced vs raw is a choice the user must make
Self-guarding entities (`Order`, `TrafficLight`) want raw mode, where an action that breaks a constraint is a bug.
Gated entities (most examples) want `.Enforced()`. The wrong choice either floods you with false bugs or hides real
ones. Today the README explains it and the failure message names the action.
- *Idea, small:* when a raw run fails, also run it enforced and say "this passes when enforced; if the entity relies on
  `Apply`, use `.Enforced()`".

### Smaller things
- Each AutoBogus-backed test on a large type takes over a second (400 start attempts and generator set-up per test).
  Caching starts per `(type, constraints)` would remove most of it.
- `RoomBooking`, `ShiftRoster`: overlap and rest-period rules are never violated by AutoBogus (empty or disjoint
  lists), so they stay hand-built.
- `EntitySpec.Exhaustive` returns the largest report of several starts; there is no combined report.

## 3. Deliberately left hand-written

- `Validate_*` tests: they are the documentation of each rule with a concrete value; a property would obscure them.
- `Channel_Applied / Noop / Failure` tests: they show `ConstrainedValue` and `ApplyDetailed`, which is the point.
- `Aggregate_*` and `Level 1/2/3` Patient tests: named, targeted violations with specific messages.
- `RoomBooking`/`ShiftRoster`/`Coupons` violating cases: need specific overlapping or conflicting data.
- Reactive example specs (`After().Then().Within()` and friends): already `Script`-driven; they gained `Coverage`.

## 4. Findings from the hostile-by-default generator

Found only after `GenAuto.Of<T>` started producing edge values (the first pass, with AutoBogus, could not):

| Domain | Finding |
|---|---|
| `Order` | `OrderLine.Subtotal` (`Quantity * Price`) **overflows `decimal`** on extreme lines; the computed `Total` then throws inside the constraint, and even `ToString` of the order throws. |
| `BankAccount` | `Deposit(-n)` is accepted by the method (negative deposit); at about 7.9e28 `Balance + 0.01` is unchanged, so a deposit has no effect; an undefined `AccountStatus` and a negative `OverdraftLimit` are accepted. |
| `TrafficLight` | `Advance()` **throws** `ArgumentOutOfRangeException` for an undefined `Signal`; no constraint rejects one. |
| every domain with an enum | No constraint rejects an **undefined enum value** (`(Status)999`), which is what a deserialiser produces from bad input. |
| `StockItem` | With `0` and `-1` in the argument domain `Restock` grows without bound: the enforced search needs a `Boundary`. |
| `DateRange` | With only early dates in the domain `Extend` can never move the end forward under `Enforced`: domains need the late extreme (now included). |
| library | A constraint that **throws** on hostile input (the `Order.Total` overflow, the `OrgChart` duplicate-id `ToDictionary`) used to abort the run; it is now reported as a violation ("X threw OverflowException") by `Valid`, `Probe`, `RejectsInvalid`, `Preserves`, `ShouldSatisfy` and `Boundaries`. |

## 5. Findings from declaring the lifecycles as machines

| Domain | Finding |
|---|---|
| `SupportTicket` | The declared machine has `AwaitCustomer` and `CustomerReplied`; the entity has no method for either, so `ConformTo` fails before any walk. `AwaitingCustomer` is unreachable in practice. |
| `SupportTicket` | **`Resolve()` is unguarded**: resolving an `Open` ticket (the machine does nothing there) lands in `Resolved`. Only `Apply` stops it. Found by `ConformTo` on the part of the lifecycle the entity does implement. |
| `Person` | The machine has `GoOnLeave` and `Return`; the entity has only `Terminate()`. `OnLeave` is unreachable in practice. |

A declared machine cannot represent guards that depend on runtime data; those stay in the entity's own constraints (`Enforced()`).

## 6. A docs-platform domain, from docs-eng-team's investigation

`examples/Nullean.Entities.Examples/Docs/DocsPlatform` expresses 16 of the constraints catalogued in the docs-eng-team domain-model investigation (`docs/domain-model/constraints.md`), plus the
product-lifecycle ordering and the link-index deadline, which it found missing. It is shaped like docs-builder's `erd-model` aggregates, which refer to each other in four ways, none of them the
`<Element>Id` convention:

| How it refers | In docs-builder | In the example | Rule |
|---|---|---|---|
| a list of id strings into another aggregate | `DocumentationSetDefinition.Products` to `Product` | `Docset.Products` to `Catalog` | `References` over the pair `DocsetInCatalog` (I2/D22) |
| an id into another aggregate | `CodexGroup` to `CodexGroupDefinition.Id` | `Docset.CodexGroup` to `CodexConfig` | `Check` over `DocsetInCodex` (D29) |
| a dictionary key that repeats the entity's name | `AssemblyConfiguration.Repositories` | `Assembly.Repositories` | `KeysMatch` |
| a URI | a mount's `Source` (`repo://path`) to a repository | `Mount.Source` to `Assembly` | `References` over `NavigationInAssembly` (G62/I6) |
| a path inside a tree | `TocItem.Children`, identity is a `DocsetPath` | `TocNode` | `Unique` over `Tree.Flatten` (D74), `..` check |
| an include graph, before expansion | `toc.yml` includes | `TocIncludes` | `Acyclic` (D69), `References` |

Also: G87 unique product ids, G89 base not after current, V1/G80 versioning system exists, N1 page URLs, D101 redirect sources, D106 redirect targets, N30 redirect chains, N25 a redirect shadows a
live page, G67 mount prefixes; a product lifecycle as a `StateMachine` (Exhaustive, `ConformTo`); N38, N48 and the link-index deadline as `Temporal` rules, and N48 proved for every command sequence with
event-sourced `Exhaustive(rules)`.

What the tests show: with the relations declared to AutoGen (`References`, `Reference`, `KeyedBy`, `Identity`), `Probe()` finds a counter-example to **every** rule from generated data alone: a dangling
product id, a mount of a repository nothing has, a dictionary key that drifted, a file listed at two depths, an include cycle. `Laws()` passes for each set, and told us something true twice: a redirect
chain cannot be written so that it breaks only `RedirectsAreNotChained` (the middle hop is either not a page or is one), and a missing root also puts every file outside it.

Layers: a cycle rule belongs on the include graph before expansion; the expanded tree cannot hold one (see `docs/integration.md`).

Not expressed: the C1-C3 compatibility rules (an old config file must keep loading), which are about files on disk and belong in golden-file tests; and the rules that need a *different repository's* data
(I3: a cross-link resolves in another repository's manifest), which would be a pair over the link registry and a manifest.

## 7. Spike: docs-builder's real domain (`feature/erd-model`)

`docs/spikes/docs-builder` is a console project (not in the solution) that references docs-builder's `Elastic.Documentation` project and this repository's packages by path, and runs them against the real
aggregates. It builds docs-builder in place and changes nothing tracked; no branch was created in that repository (this session's tooling cannot run git outside its own worktree).

**Linking.** docs-builder's domain project (net10.0, `LangVersion=preview`, `TreatWarningsAsErrors`, NativeAOT-compatible) and `Nullean.Entities.CsCheck`, `.FileSystem` and `.CsCheck.FileSystem` build and run together. No name or
version conflicts (their `Constraint<TModel>`/`Finding` and our `Constraint<T>`/`Violation` coexist; alias one).

**Generation.** `GenAuto` builds all of `GlobalCatalog`, `ProductCatalog`, `DocumentationSetDefinition`, `ResolvedDocumentationSet`, `SiteNavigationDefinition`, `AssemblyConfiguration`, `LinkRegistry`, `LinkManifest`,
`ChangelogConfiguration`, `CodexConfiguration` and `DocsetDiscovery` in all three modes (100 of 100 draws each), with no `Use<T>` and no hand-written generator except `UseFileSystem()` for the `IFileInfo`/`IDirectoryInfo`/
`IFileSystem` members. That includes their real C# `union`s (`TocItem`: all seven cases reached, no default union), typed paths (`DocsetPath`), `FrozenDictionary`, `IReadOnlySet` and `[Equatable]` records.
`ResolvedDocumentationSet.SourceFile`/`ScopeDirectory` share one file-system world in 300 of 300 draws; the source file exists in 100% (plausible), 86% (mixed) and 70% (adversarial). The default `MaxDepth` keeps trees tiny
(about 2 nodes on average, 10 at most); raise `MaxDepth` and `MaxCollection` for realistic docsets.

**Derived invariants.** `AssemblyConfiguration` derives `AvailableRepositories` and `PrivateRepositories` from `ReferenceRepositories`. Generated data cannot know that (private-is-available was violated in 300 of 300 plausible draws).
`Normalize<AssemblyConfiguration>(Derive)`, where `Derive` is the loader's own derivation, makes plausible data satisfy all five invariants written against it (0 violations), leaves about a third of mixed data and most adversarial data
breaking them, which is what the rules need to be tested against. Navigation mounts that must name an assembly repository needed the same treatment (`Normalize<NavigationInAssembly>` rewriting the mounts' schemes).

**What the generated data found in docs-builder's own code**

| Finding | Evidence |
|---|---|
| **`path-prefixes-unique` throws.** `SiteNavigationDefinition.CollectPathPrefixes` builds `new Uri($"{scheme}://{prefix}/")`, which throws `UriFormatException` for a `path_prefix` that is not a legal host. A constraint should report, not throw | threw in 113 of 300 mixed and 145 of 300 adversarial navigations |
| `DocumentationSetConstraints.All` also throws on hostile data (which rule was not pinned down) | 59 of 300 mixed, 125 of 300 adversarial |
| Nothing checks that a docset's `products` exist in the catalog (D22): none of their rules ever mentions an unknown docset product id | ours flagged one in 130 of 300 mixed and 188 of 300 adversarial pairs; theirs in 0 |
| Nothing checks that a mount names a repository the assembly has | ours flagged 151 of 300 mixed and 170 of 300 adversarial |
| Where their prefix rules do not throw, they agree with the `Unique` re-expression 1:1 | plausible: 0 and 0; mixed: 71 flagged by both, 0 by only one |

**NativeAOT.** A console app referencing their domain project, `Nullean.Entities` and `Nullean.Entities.FileSystem`, applying `Unique` and `References` to a real `SiteNavigationDefinition`, publishes NativeAOT with no IL warnings and runs.

**Performance (Release, one core).** `Unique` over `Tree.Flatten`, `Acyclic` and `References` on 11,000, 37,000 and 137,000 items: 1 to 3 ms, 4 to 11 ms, 15 to 45 ms, with up to 38 MB allocated at 137,000 items (`References`; `Unique` over a tree is 4 to 16 MB). `Acyclic` is recursive and handled a
137,000-long include chain; an include chain a million deep would not, which no real docset approaches.

**What the spike changed in the library:** `Normalize<T>`; identities declared with `Identity` are unique in plausible data (a shared depth-limit constant had been the source of "repeated" paths); depth-limit values are drawn
fresh instead of shared.

### The branch (`feature/erd-model-entities` in docs-builder, commit `fe203348c`, nothing pushed)

A new worktree off `feature/erd-model`'s HEAD (your uncommitted work there is untouched). It adds `tests/Elastic.Documentation.Entities.Tests` (not in `docs-builder.slnx`; it needs a checkout of this repository through the
`NulleanEntitiesRoot` property, and passes `NulleanSingleTfm=true` so docs-builder's stable 10.0.300 SDK can evaluate our multi-targeted projects) and two small fixes in the domain.

| What | Result |
|---|---|
| Our rules on the **shipped `config/`** (assembler and both navigation files) and on the real **`docs/_docset.yml`** | 4 tests, **0 violations**: repository dictionaries are keyed by name, private is available and listed, available repositories publish, every mount names an available repository, no toc path is listed twice, every docset product exists |
| docs-builder's own `ConstraintSetTests` laws harness, over our assembly rules through an 8-line adapter (`ConstraintSet<T>` to their `Constraint<T>`, `Violation` to `Finding`) | valid breaks nothing; each of the 6 rules has a counter-example that breaks only it. (The harness is copied verbatim into the project: TUnit does not discover inherited tests of a base class in another assembly.) |
| Every one of the 11 aggregates generated, in all three modes | passes; plausible assemblies satisfy all six rules with the loader's derivation as the normalizer |
| docs-builder's own existing suites on the branch | `Elastic.Documentation.Tests` 73 of 73, `Elastic.Documentation.Configuration.Tests` (golden snapshots included) 849 of 849 |

**Bugs found and fixed on the branch** (each has a regression test that fails without the fix; they were found by hostile generated data and are confirmed by stashing the fixes):

1. `default-cta-known` threw `ArgumentNullException` for a `default_cta` whose value is null (`ctas.ContainsKey(null)`). Fixed: a null value is an unknown cta.
2. `path-prefixes-unique` threw `UriFormatException` for a `path_prefix` that is not a legal URL path. Fixed with `Uri.TryCreate`.
3. **A .NET runtime bug.** `Uri.TryCreate("file://\u202E/", UriKind.Absolute, out _)` throws `IndexOutOfRangeException` (a `Try*` method should never throw), reproduced on a bare console app on .NET 10.0.8. A `file://` URI whose host is only U+202E (RIGHT-TO-LEFT OVERRIDE, in the adversarial edge catalog) triggers it, and it was the
   reason fix 2 first appeared not to work. The fix wraps the call. This is worth reporting to dotnet/runtime; not done here.

Left as a suggestion, not changed: an invalid `path_prefix` is now skipped by the duplicate check, so nothing reports it; a new constraint ("path_prefix is a legal URL path") would, but adding one changes their golden list and laws.

**Not done:** pushing either branch; reporting the runtime bug upstream.
