# A first-use review: what each kind of user expects, and where it stands

This was written while building the eight example apps (`examples/Apps`), after a first-use review of the library from six points of view. The apps are the evidence: each persona's expectation that turned out to be a real gap got code, and is listed under "built". What did not get code is listed under "still open", with the reason.

Three questions started it. *How does one implement a declared state machine? How are constraints used while coding, in a constructor? How are services built on the entities, and tested?* All three were runtime gaps, not missing documentation:

| Question | What was missing | What exists now |
|---|---|---|
| Implement the type-state machine | `StateMachine<TState>` could say whether `from -> to` is allowed but could not **fire** a transition: nothing built the next state, the trigger name was a label, no binding to an entity. | A factory on an edge (`To<Placed>(d => new Placed(0))`, `On<int>("AddLines").To<Placed>((p, n) => ...)`), `Fire`, `Transition` (the event to publish), `Permitted`, `CanFire`, and `machine.For<Order>(get, with)`, which fires on the aggregate and checks the result against a constraint set. `OrderFulfilment` runs the exact lifecycle that was asked about. |
| Constraints while coding | Nothing for construction: no `Ensure`, no `Create` pattern. | `ConstraintSet.Validated(value)` → `Result<T>` (errors reject, warnings ride along), `Ensure(value)` throws; `Result.Map/Bind/Select/SelectMany/Combine`. The documented place for the check is a **factory** (`Email.Create`), not the constructor, and `Apply`/`Fire` after that. |
| Services, and testing them | No service example, no DI, and every service-shaped test subject had to expose `IObservable` events. | Eight apps wired through `Microsoft.Extensions.DependencyInjection`; `Script.SampleStateAsync` (rules on the state a service exposes, no Rx), `machine.ConformsToAsync(...)` (a service against its declared machine), `StepAsync`, `ConformToServiceAsync` (value machines, async bindings). |

## The six points of view

### New to domain modelling

**Expects**: one end-to-end walk through "write an entity, a service, a test"; to be told where a rule goes; an entity that cannot be built invalid.

**Built**: `OrderFulfilment` and `Banking` are that walk, in that order, and their READMEs say what to copy. The answer to "where does a rule go" is a table:

| When | Use |
|---|---|
| A value is made | a factory returning `Result<T>` (`Validated`, `Combine` for several inputs) |
| A value changes | `Apply` / `ApplyDetailed` against a `ConstraintSet` |
| A state machine moves | `machine.Fire` / `machine.For<T>(...).Fire` |
| Data already exists | `Validate` / `ValidateAsync` (`DataAudit`) |
| Something must happen in time | a `TemporalRuleSet` (`SupportDesk`) |
| You want to know the rules are right | `Probe`, `Laws`, `Exhaustive`, `ConformsTo`, `SampleState` |

**Still open**: a generated `Create` factory. The generator emits `All` and `Actions` and never touches a constructor; whether it should emit `Create` is worth deciding after more code uses the factory pattern by hand.

### Domain-modelling expert (DDD)

**Expects**: always-valid aggregates, smart constructors, commands → transitions → domain events, repositories, consistency boundaries, rules across aggregates.

**Built**: the `Transitioned<TState>` event; the `MachineBinding` for an aggregate; `EventSourcedCart` (pure `Decide`/`Evolve`, an event store with optimistic concurrency, rules checked on the events a command would produce *before* append); `Inventory` (two aggregates and a `StockState` pair record that holds the rule between them: `Map(prefix:)`, `Merge`, `References`).

**Still open**: no outbox or saga support; consistency across aggregates in one transaction is the infrastructure's job (`Banking` says so with its `SemaphoreSlim`).

### Database administrator

**Expects**: which rules map to `CHECK`, `UNIQUE`, foreign keys; an audit of rows that already exist; to know that in-memory uniqueness is not table-wide.

**Built**: `DataAudit`: `ValidateAsync` over a stream, table rules over the kept rows, severities, a report ranked by them, the same rules on the write path (`TryInsert`), and `SchemaEquivalent`, a table mapping each rule to the schema constraint it belongs in, with the rules that have no declarative form named as such. It is checked against dirt it seeded itself.

**Still open**: no DDL export (a rule that reads a clock or spans tables has no column-level form, so a mechanical export would be partial and misleading). `Unique` and `References` over rows in memory are an audit, not a substitute for the index and the foreign key.

### Functional programmer

**Expects**: `Result` with map and bind and query syntax; accumulated errors; pure decide and evolve; no exceptions for expected failure.

**Built**: `Result.Map/Bind/Select/SelectMany` (stop at the first failure) and `Result.Combine` (keeps **every** violation: the applicative one), used by `Banking` to parse a transfer's three inputs and report everything wrong at once. `EventSourcedCart` is pure `Decide`/`Evolve`. A refused operation is a `Result.Fail`, never an exception.

**Still open**: there is no `Option`, and `Result<T>.Fail` carries the *attempted* value, which has no meaning once a failure is carried across a change of type (`Map`, `Bind`): it is then `default`. `Combine` stops at three results; more is a fold.

### Property-test expert (Hypothesis, QuickCheck, proptest, jqwik)

**Expects**: stateful testing of a real service against a model; shrunk sequences; seed replay; coverage that fails vacuous runs; parallel testing.

**Built**: `Script.SampleStateAsync` and `machine.ConformsTo` are exactly that, on CsCheck's `Sample`: failures are shrunk sequences with a replayable seed, and a service that ships an unconfirmed order is caught by both (`OrderFulfilment` tests the same checks against a deliberately sloppy service). `Coverage<TEvent>` already refuses vacuous temporal runs. `Probe` and `Laws` test the *rules*: they show that generated data can break each one, and give each a counter-example that breaks that rule alone.

**Still open**: `ConformsTo` does not explore triggers that take an argument (the machine cannot predict what an argument builds). Parallel testing of a service (`SampleParallelWith`) is Rx-based and was not rebuilt for services; `Banking` serialises with a lock and tests the rules, not the interleaving. `Probe` reports "never violated" but cannot tell a tautology from a generator that cannot reach the case: building the apps hit both (a depth rule the generator could not reach, an extension rule it never broke), and fixing them meant knowing the generator. `MaxDepth` counts a list and its element as two levels; that is now written down.

### Formal-verification student

**Expects**: an explicit transition relation; safety and liveness told apart; bounded exhaustive checking with counter-example traces; refinement of an implementation against a spec; to know what is *not* proved.

**Built**: the vocabulary maps directly, and none of it is a proof:

| In verification | Here |
|---|---|
| State invariant | `Always`, and every point-in-time constraint |
| Transition relation | the machine's edges; `Transitions`; `Allows` |
| Safety (nothing bad happens) | `Never`, `NeverTwice`, `Transitions`, sequential rules (`Conserves`, `Monotonic`) |
| Bounded liveness | `After().Then().Within()`, `AfterUntil`: a deadline on an obligation |
| Liveness (something good happens) | `Eventually`, decided only when a bounded run finishes |
| Reachability, bounded model checking | `EntitySpec.Exhaustive()` explores the state space over a finite command domain and reports whether it **closed** (`EventSourcedCart` explores every cart that can exist) |
| Counter-example trace | a shrunk sequence with a seed |
| Refinement (implementation ⊑ spec) | `ConformsTo` / `ConformTo`: the implementation is run along the spec's traces and compared step by step. Trace testing, not trace inclusion |
| Compositional rules | `ConstraintSet.Pair/Merge/Map` |

**Still open, and stated**: guards are opaque lambdas, so there is no symbolic reasoning about them; exploration needs a finite command domain; there is no fairness and no unbounded liveness; nothing is proved for inputs the generators do not produce. A TLA+ or Alloy export of a machine is possible (the graph is data) and not built.

## What building the apps found in the library

Each of these was found by writing the app or its property test, not by review:

1. The type-state machine could not run (above), and `ConstrainedValue.Enforce` does not check its initial value. That is deliberate: a channel can start over legacy data and repair it through `Apply`. Checking it first is one call (`For(set.Ensure(x))`), and the remark says so.
2. A refused step leaves a service's state unchanged, which is a *stay*, not a transition: `SampleState` asks only the point-in-time rules about a step that changed nothing, as `Apply` does.
3. Two aggregates both had a rule called `SkusAreUnique`. A merged set identifies rules by name, and `Probe` crashed on the duplicate key. `ConstraintSet.Map(project, prefix:)` exists for it, `Probe`/`Laws` now say what is wrong, and the prefix also tells a violation which part it is about.
4. `Apply` treats a result equal to the input as a no-op and runs **no** rule. Whether a tree edit returns an equal value is accidental, so an operation where "nothing happened" is an error (terminating someone who is not there) must use `ApplyDetailed`. A property test over random edits found the inconsistency; the core README and `OrgChart` explain it.
5. The generator's reach: `MaxDepth` counting, `Large()`'s documentation claiming six levels (it is about three), and no generated file with a wrong extension (`FileKind.WrongExtension` now).

## Not built, and why

- A generated `Create` factory; a DI package; DDL, TLA+ or Alloy export; symbolic guards; fairness; an outbox. Each is a decision better made after the pattern has been used by hand in real code, which is what the apps are for.
