---
navigation_title: "State machines"
description: "Entities as state machines: `EntitySpec`, the terminals, and the ways to declare a machine (declared, reducer, event-sourced, mutable)."
---

# State machines

## Entities as state machines: `EntitySpec`

An immutable entity whose methods return the next value *is* a state machine, which is what CsCheck's state-machine
engine (`Spec<S>`) explores. `[Constraints]` also generates `OrderConstraints.Actions` (every public method on
`Order` that returns `Order` and takes zero or one argument) and an `OrderConstraints.Instance` that carries both:

```csharp
OrderConstraints.Instance.Spec().Exhaustive();                  // companion class
EntitySpec.For<Order>().Exhaustive();                           // when Order declares its own constraints
```

With nothing else configured, AutoBogus supplies the **starting orders** (valid ones, one per distinct status) and the
**values each action is tried with**. Override anything:

```csharp
var spec = OrderConstraints.Instance
    .Spec(Order.New(id))                         // a starting state you care about (Order.New is the example's own factory)
    .Domain(lineA, lineB)                        // values to try for AddLine(OrderLine)
    .DomainFrom<OrderLine, string>(l => l.Sku)   // ...and for RemoveLine(string): the SKUs just added
    .Limit(collections: 3)                       // explore orders up to 3 lines (default 2)
    .Key(o => (o.Status, o.Lines.Count));        // what makes two orders "the same state"
```

### The terminals

| Call | What it does |
|---|---|
| `spec.Exhaustive()` | Visits every reachable state and step. Passing is a proof within the bounds; failing gives the shortest counterexample. Also fails if a guard or edge was never exercised. |
| `spec.Sample()` | Random walks, shrunk to the shortest failing walk. For spaces that will not close. |
| `spec.Mermaid()` | The reachable state graph as a Mermaid flowchart, for docs and review. |
| `spec.Snapshot(expected)` | Hashes the state graph. Pass `0` once and copy the hash from the failure; it changes only when behaviour does. |
| `spec.Faults()` | Mutation testing: breaks each action in turn (or the faults you `Fault(...)`), reports any no rule catches. |
| `spec.Conform(() => new Repo(), (r, step) => r.Apply(step), (r, expected) => r.Get() == expected)` | Same walks drive a real implementation and the entity; compared after every step. |
| `spec.Commutes("A", "B")` / `spec.Idempotent("A")` | Laws, from every reachable state and for every argument. |
| `spec.States()` | A `Gen<T>` of entities reached by running actions: valid by construction, shrinks to the shortest action sequence. |
| `.StepScoped("AddLine", set)` | A rule that holds for one action only. |

How the constraints become requirements:

| Constraint | Checked as |
|---|---|
| `Always`, `Check`, `Idempotent`, `NotInFuture`, `NotOlderThan` | holds in every reachable state |
| `Transitions`, `Monotonic`, `Conserves`, `CheckTransition` | holds over every step (a step that changed nothing is skipped, as `Apply` treats a `Noop`) |
| `When(guard).Requires(...)` | holds whenever the guard is true, **and** the guard must be true somewhere |
| `Transitions(selector).Allow(a, b)` | every other change is rejected, **and** each listed edge must be taken somewhere |

### Raw or enforced: the one choice you make

- **Raw (the default)** treats every action as responsible for keeping the entity valid. An action that can break a
  constraint is reported as a bug. Right for self-guarding entities like `Order.Ship()`.
- **`.Enforced()`** models the `Apply` boundary: an action whose result violates a constraint is *rejected* and the
  entity stays as it was. Right for entities with plain methods that rely on the constraints to say no. The proof then
  says "no reachable state is invalid, and every listed transition is reachable through enforced actions"; the useful
  parts are the coverage and the diagram of the lifecycle that really exists.

If a raw run reports a bug on something you know `Apply` would stop, that is the signal to add `.Enforced()`.

### Things to know

- **A finite domain is the price of a proof.** Each action is tried with each value in its argument type's domain:
  three AutoBogus values by default (all values of an enum or bool), or what you give `.Domain<T>(...)`. Keep it small.
- **The space has to close.** The default `Key` compares public properties structurally (lists element by element), and
  a state holding more than `.Limit(...)` items (default 2, or the start's own size if larger) is checked but not
  expanded. Numbers that only grow need a `.Boundary(...)`. If the space does not close, `Exhaustive` says so rather
  than passing: reaching `maxStates` (default 100,000) proves nothing.
- **Time is fixed.** Time-aware constraints read a constant clock (`.Clock(...)`), so exploration is deterministic.
  An entity that reads `DateTime.UtcNow` itself makes every step a new state; give it a `.Key(...)` that ignores the timestamp.
- **Methods with two or more parameters** are not generated as actions. Add them with `.Action("Name", state => ...)`.

Runnable versions of all of this: [`AutoTestExamples.cs`](https://github.com/nullean/entities/blob/main/tests/Nullean.Entities.Examples.Tests/AutoTests/AutoTestExamples.cs)
and the per-domain tests next to it. What did and did not convert cleanly is in [`AUTOTEST_NOTES.md`](https://github.com/nullean/entities/blob/main/AUTOTEST_NOTES.md).

## Ways to declare a state machine

The record-with-methods style above is one way. Every front-end below produces an `EntitySpec`, so the same one-liners work: `Exhaustive`,
`Sample`, `Mermaid`, `Snapshot`, `Faults`, `Conform`, `Commutes`.

| Your model is | Declare it as | Start with |
|---|---|---|
| An immutable record whose methods return the next value | `[Constraints]` generates the actions | `OrderConstraints.Instance.Spec()` |
| **A literal diagram**: states, triggers, edges | `StateMachine.Define<TState, TTrigger>(initial).Permit(...)` | `machine.Spec()`, `machine.ConformTo(entity, ...)` |
| A diagram in the docs (Mermaid) | `StateMachine.FromMermaid(text)` | the same, so the diagram is the specification |
| A reducer: `(state, command) => state` (Elm, MVU, F#) | a function and the commands | `EntitySpec.FromReducer(initial, reduce, commands, constraints)` |
| Event sourcing: `Decide(state, command) -> events`, `Evolve(state, event) -> state` | two functions | `EntitySpec.EventSourced(initial, decide, evolve, commands, constraints)` |
| A class that mutates in place (an EF aggregate, a service) | a factory and `Do(...)` actions | `EntitySpec.ForMutable(factory, constraints).Do("Open", d => d.Open())` |
| One record type per state, or a C# union (typestate) | `Transitions(x => x.GetType())` on the constraint | the record style; state is keyed by case type |

### A declared machine: the diagram is the specification

```csharp
public static readonly StateMachine<TicketStatus, string> Machine = StateMachine
    .Define<TicketStatus, string>(TicketStatus.Open)
    .Permit("Assign",          TicketStatus.Open,              TicketStatus.InProgress)
    .Permit("AwaitCustomer",   TicketStatus.InProgress,        TicketStatus.AwaitingCustomer)
    .Permit("CustomerReplied", TicketStatus.AwaitingCustomer,  TicketStatus.InProgress)
    .Permit("Resolve",         [TicketStatus.InProgress, TicketStatus.AwaitingCustomer], TicketStatus.Resolved)
    .Permit("Close",           TicketStatus.Resolved,          TicketStatus.Closed)
    .After(TimeSpan.FromDays(14), TicketStatus.Resolved, TicketStatus.Closed)   // by itself, after 14 days
    .Build();

Machine.Spec().Exhaustive();             // every state reachable, every edge taken: a state nothing leads to fails here, by name
Machine.ToMermaid();                     // the diagram, generated rather than drawn
Machine.AsConstraint<SupportTicket>(t => t.Status);   // the Transitions allow-list, so it cannot drift from the diagram

Machine.ConformTo(Open(), t => t.Status,
    bindings: [("Assign", t => t.Assign("agent-1")), ("Resolve", t => t.Resolve(now)), ("Close", t => t.Close())]);
```

`ConformTo` walks the **machine** and applies the same trigger to your real entity, asserting after each step that the entity is in the
state the machine says. It fails in two distinct ways, both useful:

- **A trigger with no binding** ("the machine declares `AwaitCustomer`, `CustomerReplied`, the entity has no way to do them") is reported
  before any walk. The declared state is one the entity can never reach. Running this on the example domains is how `SupportTicket`'s
  unreachable `AwaitingCustomer` and `Person`'s unreachable `OnLeave` show up at declaration time instead of by a missed coverage edge.
- **A divergence** ("after Resolve in Open the machine says Open, but the entity is in Resolved"): the entity does something the machine forbids.
  `SupportTicket.Resolve()` is unguarded, and only `Apply` stops it; conformance sees the method, not `Apply`.

`ConformToMutable(create, stateOf, bindings: [(trigger, d => d.Open())])` is the same for a class that changes in place. Timeouts (`After`) are
explored by `Spec()`; conformance does not test them because that needs a clock (use `ManualTimeProvider` with a temporal rule).

Mermaid in, machine out:

```csharp
var machine = StateMachine.FromMermaid("""
    stateDiagram-v2
        [*] --> Trial
        Trial --> Active : Activate
        Trial --> Expired : after 14d
        Active --> Cancelled : Cancel
        Cancelled --> [*]
    """);                                      // StateMachine<string, string>
```

### Reducer, event-sourced, mutable

```csharp
// A reducer: each command is an action; constraints on the state apply as usual
EntitySpec.FromReducer(0, (int n, string c) => c == "inc" ? n + 1 : Math.Max(0, n - 1), ["inc", "dec"], nonNegative).Boundary(n => n < 4).Exhaustive();

// Event sourcing: state is the fold of events; temporal rules apply to the events each command emits
var doors = EntitySpec.EventSourced(DoorState.Closed, Decide, Evolve, [Open, Close, Lock, Unlock], stateRules);
doors.Spec.Exhaustive();                                     // state constraints, proved
doors.Exhaustive(neverOpenedRightAfterLocked);               // the same, plus temporal rules over the emitted events, for every command sequence
doors.SampleEvents(neverOpenedRightAfterLocked);             // random walks: for rules that cannot be proved by a bounded search

// A mutable class: rebuilt by replaying the actions on a fresh instance, so no clone method is needed
EntitySpec.ForMutable(() => new MutableDoor(), rules.Map<MutableDoor>(d => new Door(d.State)))
    .Do("Open", d => d.Open()).Do("Lock", d => d.Lock())
    .Exhaustive();
```

Notes: `doors.Spec.Exhaustive()` proves the **state** constraints. `doors.Exhaustive(rules)` also proves temporal rules over the emitted events: it explores
the product of the folded state and each rule's monitor (open obligations, with their deadlines relative to now, are part of the state), advancing a
virtual clock by `stepInterval` per command, so a violation comes with the shortest command sequence. A command that emits nothing still lets time pass,
so a bounded-response rule is refuted by the commands that "wait". It supports `Never`, `NeverTwice`, `Always`, `Transitions`, `When` and
`After().Then().Within()`; `Eventually` and `Required()` only resolve when the stream ends, so they are rejected (not silently skipped) and belong in
`SampleEvents`. Events are told apart by `ToString()`, so use records. Use `Transitions` (raw adjacency) rather than `Never<A, B>`
for "A must not directly precede B": `Never` ignores events of other types in between. A mutable class costs a replay per transition, fine for the
bounded searches `Exhaustive` does, so give it a `snapshot` that says what makes two states the same (default: public properties, structurally).
`ConstraintSet.Map(project)` lifts constraints written for one type onto another (a wrapper, a projection) while keeping names, guards and edges.
