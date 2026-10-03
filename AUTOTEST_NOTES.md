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

`examples/Nullean.Entities.Examples/Docs/DocsPlatform` expresses 13 of the constraints catalogued in the docs-eng-team domain-model investigation (plus the product-lifecycle ordering and the link-index deadline, which it found missing) (`docs/domain-model/constraints.md`),
shaped like docs-builder's `erd-model` aggregates (`Catalog`, `Product` with a lifecycle machine, `DocumentationSet`, `Site`, a pipeline event log). Tests are in
`tests/Nullean.Entities.Examples.Tests/Docs`.

| Kind of rule | Rules written | How |
|---|---|---|
| Uniqueness | G87 product ids (case-insensitive), N1 page URLs, D101 redirect sources, G67 path prefixes | `Unique` |
| Referential integrity | V1/G80 product to versioning system, D106 redirect target exists, G62/I6 mount to toc, includes to tocs | `References` |
| Graph | D69 toc include cycles, N30 redirect chains | `Acyclic`, `NoChains` |
| Cross-field | G89 base not after current, N25 redirect shadows a live page | `Always` with a selector, `Check` |
| Lifecycle | the ordering of preview, beta, GA, deprecated and removed, which the investigation found nowhere | `StateMachine`, `ConformTo`, `Exhaustive` |
| Temporal | N38 registry only moves forward, N48 publish only after links validated, link index updated within 10 minutes of a green build | `Temporal<T>`, `Trace.Check` |
| Temporal, for every command sequence | the publish step cannot publish an unvalidated manifest | event-sourced `Exhaustive(rules)` |

What the tests show: `Probe()` finds a counter-example to every rule from **generated data alone** (duplicates, dangling references, include cycles, redirect
chains, inverted versions), with no hand-written generator. `Laws()` passes for each set, and it told us something true about the rules: a redirect chain cannot be
written so that it breaks only `RedirectsAreNotChained`, because the middle hop is either not a page (breaks `RedirectTargetsExist`) or is one (breaks `RedirectsDoNotShadowPages`).

Not expressed: the C1-C3 compatibility rules (an old config file must keep loading), which are about files on disk and belong in golden-file tests, and the rules that need
a *different repository's* data (I3: a cross-link resolves in another repository's manifest), which would be a composite over the link registry.
