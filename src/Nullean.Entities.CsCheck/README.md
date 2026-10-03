# Nullean.Entities.CsCheck

Test your domain by describing what must always be true, then let a machine try to break it.

You write **constraints** on your entities once (with [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities)).
This package turns them into [CsCheck](https://github.com/AnthonyLloyd/CsCheck) tests: random inputs, exhaustive search
of every reachable state, mutation testing of the rules themselves, race detection, and more. Usually one line each.
When something breaks, CsCheck shrinks it to the smallest failing case and prints a seed that replays it exactly.

- [The idea in one minute](#the-idea-in-one-minute)
- [Two primers](#two-primers): entities and constraints, and property-based testing with CsCheck
- [Choosing a test: the CsCheck methodologies](#choosing-a-test-the-cscheck-methodologies)
- [Testing qualities](#testing-qualities-what-kind-of-bug-is-this-test-for): absent, temporal, vacuity, liveness, idempotency, monotonicity, conservation, confluence
- [Entities as state machines](#entities-as-state-machines-entityspec)
- [Simple property tests](#simple-property-tests)
- [Event streams and time](#event-streams-and-time-scripttsut)
- [Generated data and AutoBogus](#generated-data-and-autobogus)
- [When a test fails, or won't run](#when-a-test-fails-or-wont-run)

## The idea in one minute

```csharp
// 1. Your domain: an immutable record. Every method returns the next value.
public sealed record Order(Guid Id, OrderStatus Status, IReadOnlyList<OrderLine> Lines)
{
    public Order Confirm() => Status == OrderStatus.Pending && Lines.Count > 0 ? this with { Status = OrderStatus.Confirmed } : this;
    public Order Ship()    => Status == OrderStatus.Confirmed ? this with { Status = OrderStatus.Shipped } : this;
    public Order AddLine(OrderLine line) => Status == OrderStatus.Pending ? this with { Lines = [.. Lines, line] } : this;
}

// 2. Its rules, written once. [Constraints] generates OrderConstraints.All, .Actions and .Instance.
[Constraints]
public static partial class OrderConstraints
{
    public static readonly Constraint<Order> ConfirmedOrdersHaveLines =
        Constraints<Order>.When(o => o.Status == OrderStatus.Confirmed).Requires(o => o.Lines.Count > 0, "A confirmed order needs a line.");

    public static readonly Constraint<Order> LegalStatusTransitions = Constraints<Order>
        .Transitions(o => o.Status)
        .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
        .Allow(OrderStatus.Confirmed, OrderStatus.Shipped);
}

// 3. Tests. No inputs, no assertions: the constraints are the assertions.
OrderConstraints.Instance.Spec().Exhaustive();                     // every reachable state and step, proved
OrderConstraints.All.Preserves(OrderConstraints.Actions);          // random starts: every action keeps an order valid
OrderConstraints.All.ForAll(o => o.Total >= 0);                    // your own property, over valid generated orders
OrderConstraints.Instance.Spec().Mermaid();                        // the lifecycle, drawn
```

The third block never mentions an order id, a line, or a status. AutoBogus builds the starting orders and the
argument values, `Exhaustive` walks every sequence of actions from them, and the constraints decide what counts as
a bug.

## Two primers

### Entities and constraints in two minutes

A **constraint** is a named rule on a type. `Always(o => ...)` must hold in every state, `Transitions(...).Allow(a, b)`
lists the legal moves of a status, `When(guard).Requires(...)` is "if X then Y", and `Idempotent`, `Monotonic`,
`Conserves` name common laws. A **constraint set** is the ordered collection; `[Constraints]` on a static partial
class gathers every `Constraint<T>` field into `All` for you.

```csharp
OrderConstraints.All.Validate(order);                                    // is this order valid right now? returns violations
ConstrainedValue.For(order).Enforce(OrderConstraints.All).Apply(o => o.Ship());   // only commit if every rule still holds
```

`Apply` is the important one: you propose a change, the constraints judge the result, and a bad change is
**rejected** and leaves the value untouched. Some entities protect themselves (`Ship()` above does nothing unless the
order is confirmed). Others have plain methods and rely on `Apply` to say no. Both are fine, and the tests below have a
switch for each (see [raw vs enforced](#raw-or-enforced-the-one-choice-you-make)).

### Property-based testing and CsCheck in two minutes

An example test says "for *this* input, expect *that* output". A **property** says "for *every* input of this kind,
this must hold", and a library generates hundreds of inputs hunting for one that breaks it. CsCheck is that library.
Four ideas carry everything below:

- **Generators.** `Gen<T>` makes random values. They compose: `Gen.Int[1, 10].Select(n => ...)`.
- **Shrinking.** When a property fails, CsCheck keeps simplifying the input until it is the smallest one that still
  fails. You get "Place, Ship" not a 40-step walk.
- **Seeds.** A failure prints `Set seed: "..." or -e CsCheck_Seed=...`. Pass `seed:` (or set the variable) and the
  exact failing run replays, every time, on any machine.
- **Methodologies.** "Generate random values" is only the first of CsCheck's techniques. It also does model-based
  testing, metamorphic testing, parallel (linearizability) testing, exhaustive state search, mutation testing of a
  specification, and hash-based regression. Each answers a different question; the next section maps them.

The usual obstacle is that every technique needs *something to assert*, written again for each. Here the assertion is
the constraint set you already have, so switching technique is switching one method call.

## Install

```sh
dotnet add package Nullean.Entities.CsCheck
```

## Choosing a test: the CsCheck methodologies

| You want to know | CsCheck methodology | The call here | Finds |
|---|---|---|---|
| Is my own rule true for all valid inputs? | Property-based sampling (`Sample`) | `set.ForAll(property)` | A counterexample to your rule, shrunk |
| Does my factory / parser / mapper only produce valid values? | Property-based sampling | `gen.ShouldSatisfy(set)` | The smallest invalid output |
| Can *any sequence* of actions reach an invalid state? | Exhaustive state search (`Spec.Exhaustive`) | `spec.Exhaustive()` | A shortest counterexample, or a **proof** within the bounds |
| Same, but the state space is too big to close | Model-based random walks (`Spec.Sample`) | `spec.Sample()` | The shortest failing walk it can find |
| Does every action leave a valid entity valid, from starts I did not think of? | Property-based sampling over actions | `set.Preserves(actions)` | Actions that rely on someone else to keep rules |
| Does the order of two actions matter? | Metamorphic testing (`SampleMetamorphic`) | `spec.Commutes("A", "B")`, `Script.Metamorphic` | Order-dependent bugs |
| Is an action safely repeatable? | Metamorphic testing | `spec.Idempotent("Cancel")` | Double-submit bugs |
| Are my *constraints* good enough? | Mutation testing of the spec (`Spec.Faults`) | `spec.Faults()` | A broken action no rule notices |
| Does my real implementation match the model? | Conformance / model-based (`Spec.Conform`) | `spec.Conform(create, apply, agrees)` | A repository, cache or API that drifts from the entity |
| Is my service safe under concurrency? | Linearizability (`SampleParallel`) | `script.SampleParallelWith(...)` | Races that break a rule |
| Does a deadline or timeout hold for any timing? | Property sampling with virtual time | `script.SampleWith(...)` with `.Delays(...)` | Rules that fail only at an unlucky gap |
| Does my entity follow the lifecycle diagram? | Conformance (`Spec.Conform`) against a declared machine | `machine.ConformTo(entity, ...)` | A state the entity cannot reach; a trigger it performs when it should not |
| Did behaviour change by accident? | Regression by hash (`Check.Hash`) | `spec.Snapshot(expected)` | Changed lifecycles, with the new hash to commit |
| Did my green test prove anything? | Coverage / classification | `Coverage<TEvent>`; built into `Exhaustive` | A constraint that held because it never ran |
| Which of my rules can this data even break? | Property sampling, inverted | `set.Probe()`, `set.RejectsInvalid()` | Tautologies, contradictions, unnamed rules |
| Where do my rules stop saying no? | Boundary-value analysis over an edge catalog | `set.Boundaries()` | Hostile values nothing rejects: `" "`, `NaN`, an undefined enum, an empty Guid |

Rule of thumb: start with `spec.Exhaustive()`. If the state space will not close, `spec.Sample()`. Add `Faults()`
once, to find out how much your rules really cover.

## Testing qualities: what kind of bug is this test for?

A *testing quality* is a named shape of rule. Declaring the shape (not a bare predicate) is what lets the
right technique test it, and makes the failure read "not idempotent" instead of "rule 7 failed".

| Quality | Declare it with | Tested by |
|---|---|---|
| Absent: this must never follow that | `Never<T1,T2>` (`Nullean.Entities.Reactive`) | `script.SampleWith` |
| Temporal: this must follow that, in time | `After().Then().Within()` (`Reactive`) | `script.SampleWith` + `.Delays(...)` |
| Vacuity: the rules were actually exercised | `Coverage<TEvent>` here; automatic in `Exhaustive` | `SampleWith(..., coverage)`, `spec.Exhaustive()` |
| Liveness: it eventually happens | `Eventually<T>` (`Reactive`) | `script.SampleWith` |
| Idempotency: twice is once | `Idempotent(action)` (`Nullean.Entities`) | `Validate`, `spec.Exhaustive()`, `spec.Idempotent()` |
| Monotonicity: it only moves one way | `Monotonic(selector, direction)` | `spec.Exhaustive()` (step-scoped) |
| Conservation: nothing created or destroyed | `Conserves(selector)` | `spec.Exhaustive()` on a pair-valued entity |
| Confluence: order does not matter | the law itself | `spec.Commutes(...)`, `Script.Metamorphic` |

### Absent: a constraint already catches an illegal ordering

The first quality is the simplest. You do not write an assertion; the constraint is the assertion, and the script
supplies the sequences:

```csharp
// A small pool of ids so steps collide on the same order (see "Why the ID pool matters" below).
static readonly Gen<Guid> OrderIds = Gen.Int[1, 3].Select(i => new Guid(i, 0, 0, new byte[8]));

var script = Script.For<OrderService>()
    .Step("Place",   OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship",    OrderIds, (svc, id) => svc.Ship(id));

script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All);
```

`OrderStateSpec.All` contains `NeverShippedWithoutConfirmation`. The script produces random walks of
Place/Confirm/Ship over three ids; every event the service raises is checked against the constraint; the first
violation fails the test with the shrunk walk and a seed. Nothing CsCheck-specific beyond the script's steps.

**Why the ID pool matters.** `Gen.Guid` looks like the obvious id generator, and it makes this test pass while testing
nothing: a random GUID never matches an order `Place` created, so `Confirm` and `Ship` are silent no-ops and the rule
is never exercised. Three ids make steps collide. That a green test can prove nothing is the most common
property-testing mistake, which is what [vacuity](#vacuity-a-green-test-that-proved-nothing) catches.

### Temporal: the same walk, with generated delays

Deadlines ("a confirmed order ships or is cancelled within 60 seconds", "a retry waits at least two seconds") are
where hand-written tests are weakest: you pick one delay and hope. Add `.Delays(...)` and CsCheck picks them,
including ones you would not have thought to try.

```csharp
// The rule, declared once on the domain (Nullean.Entities.Reactive):
public static readonly ReactiveConstraint<OrderStateEvent> ConfirmedEventuallyTerminates =
    After<OrderStateEvent.Confirmed>()
        .Then<OrderStateEvent.Shipped, OrderStateEvent.Cancelled>(
            m1: (confirmed, shipped)   => shipped.OrderId   == confirmed.OrderId,
            m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId)
        .Within(TimeSpan.FromSeconds(60));

// The test: the same steps, plus a generated pause before every step.
var script = Script.For<OrderService>()
    .Step("Place",   OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship",    OrderIds, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 72 * 3600].Select(s => TimeSpan.FromSeconds(s)))
    .Length(2, 12);

script.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderStateSpec.All);
```

What happens under the hood, and how to get it right:

- **The clock is virtual.** `SampleWith` hands your factory an Rx `HistoricalScheduler`. Before each step it advances
  that scheduler by the step's generated delay, so a 72-hour gap costs nothing and the test is fast and deterministic.
- **Your SUT must use that scheduler for time.** Pass it to anything that waits or timestamps
  (`Observable.Timer(..., scheduler)`, `scheduler.Now`). A service that calls `DateTime.UtcNow` or `Task.Delay` is
  invisible to virtual time, and a missed deadline can never happen. Time-aware constraints in the core package
  (`NotInFuture`, `NotOlderThan`, `Always((x, now) => ...)`) read the same virtual clock when attached with
  `ToReactive()`; give them a `TimeProvider` directly in `Validate`/`Apply`.
- **Delays shrink.** A failure reports the smallest gap that still breaks the deadline: 61 seconds, not 41 hours.
- **Pick a range that straddles the deadline.** `Gen.Int[0, 5]` against a 60-second deadline can never fail, which is
  a vacuous test again. Use a range that produces gaps well under and well over the limit, and add `Coverage`.
- **A pending deadline at the end of a run is not a violation.** Delays apply *before* each step, never after the last,
  so an obligation still open when the walk ends stays `Pending`. To check the final deadline, end the script with a
  step whose pre-delay can cross it (a no-op `Step("Wait", _ => { })` works), or use `Eventually<T>`.

**Without Rx.** If the service takes a `TimeProvider` and records timestamped events, the same script drives a `ManualTimeProvider` and
checks core temporal rules (`Temporal<TEvent>`, from `Nullean.Entities`) instead:

```csharp
script.SampleWith(
    time => new OrderDesk(time),              // the SUT reads time from the provider it is given
    desk => desk.Log,                         // IEnumerable<Timestamped<DeskEvent>> it recorded
    rules,                                    // a TemporalRuleSet<DeskEvent>
    coverage);                                // optional: the same vacuity check
```

The generated delays advance the `ManualTimeProvider`, a violation fails with the rule, its time and evidence, a shrunk walk and a seed, and
nothing depends on Rx. The Rx-based `SampleWith(IScheduler ...)` above and this one check the same operators, and a differential test keeps them in agreement.

For entities rather than event streams, time enters as a `TimeProvider`: `NotOlderThan(selector, maxAge)` and
friends take the clock from the call, and `EntitySpec` fixes it (`.Clock(...)`) so exploration is deterministic.

### Vacuity: a green test that proved nothing

A constraint like `NeverShippedWithoutConfirmation` passes trivially if no run ever ships. The same goes for
`After<Confirmed>().Then<Shipped, Cancelled>().Within(60s)` when no run ever confirms. "Green" means "no violation
found", which is only evidence if the interesting events happened. Vacuity checking makes the test fail when they did not.

**For event streams**, name the events that must show up:

```csharp
var coverage = new Coverage<OrderStateEvent>()
    .Expect("Confirmed", e => e is OrderStateEvent.Confirmed)                    // seen in at least one run
    .Expect("Shipped",   e => e is OrderStateEvent.Shipped, minPercentOfRuns: 5) // ...and in 5%+ of runs
    .Expect("Cancelled", e => e is OrderStateEvent.Cancelled);

script.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderStateSpec.All, coverage, writeLine: Console.WriteLine);
```

`writeLine` prints how often each event occurred, and an unmet expectation throws `VacuousPropertyException` even
though no constraint was violated:

```
Coverage over 100 runs:
  Confirmed                          61 runs (61.0%)
  Shipped                            23 runs (23.0%)
  Cancelled                          34 runs (34.0%)

The sample passed vacuously: "Shipped" never occurred.
Widen the script (more steps, a longer Length, different Delays) or fix the generator so these events are reachable.
```

- Add one `Expect` for the **trigger** of every temporal constraint (`Confirmed` for `After<Confirmed>()...`) and one for
  the **thing it promises or forbids** (`Shipped`).
- Use `minPercentOfRuns` when rare is not good enough: a deadline crossed in 2% of runs is thin evidence.
- A real violation still fails as a normal CsCheck failure with a shrunk walk and seed; coverage is only checked once
  the sample otherwise passes. It is skipped when you pass `seed:` (one replayed run is too small a sample).
- This is "not seen in N runs", not "provably unreachable".

**For entities**, the same idea is built in and it is stronger, because the search is exhaustive. `spec.Exhaustive()`
fails when a `When(guard)` is never true, or an `Allow(a, b)` edge is never taken, or no action ever changed the
entity. When it closes the space, an unreached edge is *provably* unreachable. This is how running it on the example
domains found that a support ticket's `AwaitingCustomer` state had no action that produced it.

### Liveness: unconditional, unlike `After().Then().Within()`

```csharp
Constraints<OrderEvent>.Eventually<OrderEvent.Shipped>(
    scope: e => e.OrderId, from: e => e is OrderEvent.Placed,
    "Every placed order eventually ships."
);
```

`After().Then().Within()` needs a bounded trigger. This is "did every instance we opened eventually close", which means
tracking an open set across the whole walk. It resolves only on the stream's `OnCompleted` (unbounded liveness is
undecidable mid-stream), so it is for a bounded run (a CsCheck walk, a recorded session), not a live feed.

### Idempotency: name the quality instead of hiding it in a predicate

```csharp
[Constraint]
public static readonly Constraint<Order> CancelIsIdempotent =
    Constraints<Order>.Idempotent(o => o.Cancel(), "Cancelling twice must leave the order unchanged.");
```

`Idempotent(action)` applies `action` once and twice and asserts the results are equal: `f(f(x)) == f(x)`. It is
checked in every state by `Validate` and `Exhaustive`. To check an action *from every reachable state, for every
argument* without declaring a constraint at all, use `spec.Idempotent("Cancel")`. A dedicated shape over `Always(...)`
means the failure says "not idempotent".

### Monotonicity: step-scoped, not global

```csharp
[ConstraintIgnore]
public static readonly Constraint<Order> TotalNeverShrinksOnAddLine =
    Constraints<Order>.Monotonic(o => o.Total, MonotonicDirection.NonDecreasing);

// attach it only to the action it is true for:
spec.StepScoped("AddLine", OrderStepConstraints.AddLineStep);
```

`Monotonic` is *sequential*: it compares the value before and after a step, so it fires from `Apply`/`Exhaustive`, never
from `Validate`. A rule like "total never decreases" is true of `AddLine` and false of `RemoveLine`, so put it in a
step set (`[ConstraintIgnore]` keeps it out of `All`) and attach it with `StepScoped`. The examples that forgot (a
bank's `DepositIsMonotonic` in the general set) make every `Withdraw` fail; see `AUTOTEST_NOTES.md`.

### Conservation: nothing created or destroyed

```csharp
public static readonly Constraint<(Account From, Account To)> TotalIsConserved =
    Constraints<(Account From, Account To)>.Conserves(p => p.From.Balance + p.To.Balance, "A transfer must not create or destroy money.");
```

Conservation usually spans two halves of a transfer, so the entity is a pair. Pair-valued entities have no generated
actions (the move is a function of the pair and an amount), so declare it by hand:

```csharp
TransferConstraints.All
    .Spec((From: new Account("DE01", 200m), To: new Account("DE02", 100m)))
    .Action("Move", [10m, 50m, 500m], Transfers.Move)
    .Boundary(p => Math.Abs(p.From.Balance) < 2_000m)    // balances can drift, so bound the search
    .Exhaustive();
```

### Confluence: two paths, compared on fresh instances

```csharp
Script.For<Order>().Metamorphic(
    factory: () => new Order(),
    gen:     genLine,                 // a Gen<OrderLine> you define
    path1:   (o, a, b) => { o.AddLine(a); o.AddLine(b); },
    path2:   (o, a, b) => { o.AddLine(b); o.AddLine(a); },
    equal:   (o1, o2) => o1.Total == o2.Total && o1.Lines.ToHashSet().SetEquals(o2.Lines));
```

`a` and `b` are generated and shrunk independently each iteration, each applied to its own fresh instance, so a failure
is "CsCheck found a pair that does not commute", shrunk to the smallest. For immutable entities this is
`spec.Commutes("AddLine", "AddLine")`, from every reachable state.

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

## Simple property tests

No state machine needed. The constraints are the oracle and AutoBogus supplies the values; CsCheck shrinks any failure
and prints a seed.

```csharp
OrderConstraints.All.ForAll(o => o.Cancel().Cancel() == o.Cancel());     // a property over valid orders
OrderConstraints.All.Preserves(OrderConstraints.Actions);                // every action keeps a valid order valid
OrderConstraints.All.Probe();                                            // which constraints can this generator break?
OrderConstraints.All.RejectsInvalid();                                   // invalid values are named and rejected
```

| Call | Checks | Use it when |
|---|---|---|
| `ForAll(property)` | `property` holds for every generated value that satisfies the constraints | You have a rule of your own (`Total == sum of lines`) and want valid inputs for free. |
| `Preserves(actions)` | From a valid start, any action with any generated argument gives a result that satisfies the constraints. The action runs **raw**, not through `Apply`, which would reject a bad result and so could never show one | You want the random cousin of `Exhaustive` that also starts from states `New()` never produces. |
| `Probe()` | Per point-in-time constraint, how often it held and was violated over 1,000 generated values | A constraint that **never fails** may be a tautology or a derived value; one that **never holds** is a contradiction (or something the generator cannot build) and throws. |
| `RejectsInvalid()` | Values violating a constraint appear in `Validate` under its name, and `Apply` to them is refused naming it | You want proof every rule is named and enforced. Constraints the generator never violates are listed as unfalsifiable. |
| `Laws(valid, counterExamples)` | Four laws in one line: a valid value breaks nothing; every rule has a name and names are unique; every rule has a **counter-example** (yours, or generated from a single-property edge value that only that rule rejects); each counter-example breaks **only its own rule** | You want a constraint set to be trustworthy: no rule that can never fire, no two rules that always fire together. It reports every failure at once and tells you when two rules overlap so a single cause cannot be isolated. |

`ForAll` and `Preserves` take `gen:`/`start:` to replace the default (AutoBogus, kept only if valid). For types whose
valid values are hard to hit by filtering, use the entities the actions can reach instead:

```csharp
var orders = OrderConstraints.Instance.Spec().States();
OrderConstraints.All.ForAll(o => o.Total >= 0, gen: orders);
```

And to test a factory or parser against the rules the domain already declares:

```csharp
Gen.Int[1, 10].Select(n => Build(n)).ShouldSatisfy(OrderConstraints.All);    // fails naming the violated constraints, shrunk
```

## Event streams and time: `Script<TSut>`

When the thing under test is a service that raises events (not a pure entity), describe the sequences it can be
driven through and check a *reactive* constraint set against what it emits:

```csharp
var script = Script.For<OrderService>()
    .Step("Place",   OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship",    OrderIds, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
    .Length(1, 20);                                   // default 0-10 if omitted

script.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderStateSpec.All);
```

`Step(name, action)` is a fixed step; `Step(name, gen, action)` generates a fresh, independently shrinkable argument
each time it is picked. `Delays` draws a `TimeSpan` before every step; with none configured, steps run back to back.
Each generated run is a fresh SUT and a shrinkable array of `(delay, step)` pairs.

| Call | Does |
|---|---|
| `script.SampleWith(factory, events, constraints)` | Random runs; fails on any violation, with a shrunk walk and seed. |
| `script.SampleWith(..., coverage)` | The same, plus [vacuity](#vacuity-a-green-test-that-proved-nothing) checks. |
| `script.SampleParallelWith(...)` | Runs steps concurrently and fails if an interleaving breaks a constraint that no sequential order does. Your SUT must tolerate concurrent calls; that is what is tested. Delays are not used (virtual time is single-threaded). Use alongside `SampleWith`. |
| `script.RunWith(...)` | The whole run as an `IObservable<EntityEvent>` (`IterationStarted`, `ConstraintResolved`, `SmallerFailure`, `Completed`), for a caller who wants the play-by-play instead of an exception. |
| `script.Metamorphic(...)` | Two paths, compared; see [confluence](#confluence-two-paths-compared-on-fresh-instances). |

If the event type implements `IHaveConstraints<TEvent>`, the constraint argument can be dropped. There is no
hand-rolled shrinking anywhere: `SampleWith`, `RunWith` and `Metamorphic` build a real `Gen<T>` and hand it to
CsCheck's `Sample`, so generation, shrinking, multithreading and seed replay are CsCheck's own.

## Generated data: plausible and hostile by default

`GenAuto.Of<T>()` builds a value of any type from its shape (constructor, properties, collections, enums, nullability) and mixes
**plausible** data with **edge cases** by default. You do not write the adversarial generator: it is already in.

```csharp
GenAuto.Of<Order>()                          // Mixed (default): mostly plausible, about one leaf in four an edge value
GenAuto.Of<Order>(AutoMode.Plausible)        // words, small positive numbers, dates near 2026: what a demo would show
GenAuto.Of<Order>(AutoMode.Adversarial)      // edge values only, including the hostile group
GenAuto.Of<Order>(AutoMode.Mixed, o => o.Use(Gen.Const(OrderStatus.Pending)).AddStringEdges("N/A"))
```

What the edge values are (each list is ordered **simplest first**, so a failure shrinks to the simplest hostile value that breaks the rule):

| Type | Edge values |
|---|---|
| `string` | `""`, `" "`, tabs, newlines, `\r\n`, leading/trailing/internal spaces, NBSP, ideographic and thin spaces, U+2028/2029, zero-width space/joiner, BOM, soft hyphen, bidi overrides (trojan source), C0/C1 control characters, DEL, private-use, noncharacters; emoji, ZWJ sequences, skin tones, flags, combining marks and "Zalgo", NFC vs NFD (`"é"` vs `"e\u0301"`), CJK, Arabic, Hebrew; Turkish `İ`/`ı`, `ß`, ligatures; homoglyphs and fullwidth forms; `"null"`, `"NaN"`, `"0x10"`, `"1e5"`, `"00123"`, ISO dates and GUIDs as text; SQL, HTML, format-string, template, JNDI, path-traversal, CSV-formula and URL-encoding payloads; email- and URL-shaped fragments (`"a@"`, `"a@@b.c"`, `"http://"`); lengths 50/100/255/256/1000/4000/4001/10,000; `null` for nullable. **Adversarial only:** NUL and lone surrogates |
| integers (every width) | `0`, `1`, `-1`, `min`, `max`, `min+1`, `max-1`, powers of two and one either side (127/128, 255/256, 32767/32768, 65535/65536, 2^31, 2^53+1), 9/10/11, 99/100/101, 999/1000/1001 |
| `double`, `float` | `NaN` (several payloads), `+/-Infinity`, `-0.0`, `epsilon`, subnormals, `Max`/`Min`, `2^53` and `2^53+1`, `0.1`, `0.2`, `0.1+0.2`, `1e-7`, `1e21` |
| `decimal` | `0`, `+/-1`, `+/-0.01`, `0.005`/`0.015` (rounding), `Max`/`Min`, `1e-28`, **the same number at different scales** (`1`, `1.0`, `1.00`, `1.000000000`), `-0.0` |
| dates and times | `default`, `MinValue`, `MaxValue`, one tick either side, Unix epoch, Y2038, leap days, 1900 (not a leap year), DST gaps and overlaps, offsets `-12:00`, `+14:00`, `+05:45`; `TimeSpan` zero, negative, 24h, 24h + 1 tick, `Max`/`Min` |
| `Guid`, `Uri` | `Guid.Empty`, all-`F`, v1/v7-shaped; relative, scheme-only, userinfo, IDN, port 0 and 65535, 2,000+ characters |
| enums | every defined value **and undefined ones** (`(E)999`, `(E)-1`, `(E)int.MaxValue`) |
| collections, dictionaries | empty, one item, duplicates, the same instance twice, keys differing only by case or whitespace |
| **relations between fields** | `Start == End`, `Start > End`, `End == Start + 1 unit` for pairs like `Start/End`, `From/To`, `Min/Max`, `Open/Close`; **duplicate identities** across the items of a list (`Id`, `Sku`, `Email`: same, different case, padded) |

Plausible strings come from a **corpus** built once from Bogus (words, names, emails, urls, cities, ibans, EAN13) and picked by index,
matched to the member name (`Email` gets emails, `City` cities). That keeps data realistic without Bogus's global lock on the hot path
(see below), and a smaller index is a shorter string, so it shrinks.

A constructor that throws on a hostile value is treated as correct behaviour: the generator redraws (20 tries) instead of failing. A
**constraint** that throws on hostile data is reported as a violation ("`TotalMatchesLines` threw `OverflowException`"), not an abort:
a rule that crashes instead of reporting is a finding. Values whose `ToString` throws are printed safely in failure messages.

### What you get out of the box

| Where | What is generated | Override with |
|---|---|---|
| `Spec()` with no start | Plausible, valid entities from fixed seeds, covering every enum/bool value at least once, up to twelve | `Spec(start)`, `.From(a, b)`, `.Data(mode)` |
| An action taking an argument | Two plausible values plus the edge values that matter most for the type (`""` and `" "`, `0` and `-1`, `NaN`, the earliest and latest date); all values of an enum or bool | `.Domain<T>(...)`, `.DomainFrom<S, T>(...)`, `.Data(mode)` |
| `ForAll`, `Preserves` | `Mixed` entities, kept only if valid | `gen:` / `start:` / `data:` |
| `Probe`, `RejectsInvalid` | Half `Mixed`, half `Adversarial`, unfiltered, so violations occur | `gen:` / `raw:` |
| `Boundaries()` | Every edge value, one property at a time, on a valid baseline | `baseline:` |

### Boundaries: where do the rules stop saying no?

```csharp
OrderConstraints.All.Boundaries(writeLine: Console.WriteLine);
```

takes a valid value, swaps in each edge value for **one property at a time**, and reports which rules reject it and, more usefully,
which hostile values **nothing** rejects. It reads like the documentation the constraints never wrote:

```
Boundaries from BankAccount { Iban = NL00TEST, Balance = 31.2, OverdraftLimit = 40.5, Status = Active }:
  Iban: 0 of 173 edge values rejected, 62 hostile values accepted
      GAP, nothing rejects: "", " ", "\u0009", "\u000A", "\u000D\u000A", "\u000D", "\u000C", "\u000B", and 54 more
  Balance: 1 of 25 edge values rejected, 6 hostile values accepted
      rejected by BalanceAboveLimit: -79228162514264337593543950335
      GAP, nothing rejects: -1, -0.01, -0.005, -0.0000000000000000000000000001, 79228162514264337593543950335
  (OverdraftLimit: the same shape as Balance)
```

Use `report.Gaps` to assert, `report.RulesFor("Balance")` to see which rules guard a property.

Know the limits:

- **A random string argument is almost always a no-op.** `RemoveLine(sku)` with a corpus word never matches a line that exists.
  Use `.DomainFrom<OrderLine, string>(l => l.Sku)` so the argument comes from the state.
- **Hostile data finds hostile bugs.** Zero-config now tries `Reserve(-1)`, an undefined enum, `decimal.MaxValue`. Expect findings in
  code that never saw them (an overflow in a computed `Total`, an unguarded `Deposit(-5)`). Use `data: AutoMode.Plausible` /
  `.Data(AutoMode.Plausible)` for a domain you have not hardened yet, and treat the rest as a to-do list.
- **Tight constraints still defeat zero-config.** A realistic email, a balanced journal entry, plausible vitals: the default reaches
  some by luck (`"a@b..c"`) but not reliably. The error names the constraints that rejected everything; give the test a start
  (`Spec(start)`), `Use<T>(gen)`, or a Bogus rule (`f.Internet.Email()`).
- **Abstract types and interfaces** are built as one of their concrete subtypes: the `[JsonDerivedType]` list when there is one, otherwise every concrete, non-generic subtype in the type's
  own assembly (nested records included). Framework types are never searched, and an interface nothing implements still asks for `AutoOptions.Use<T>(gen)`.
- **Not built by AutoGen:** classes with neither a public constructor nor a static `From`/`Create`/`Of`/`New`/`Parse` factory. Everything else is: immutable, frozen and read-only collections, `System.Tuple`, `KeyValuePair`, value tuples,
  `IPAddress`, `Version`, `Int128`, `UInt128`, `BigInteger`, `Half` and `Rune`.
- **Value objects and typed ids** are built the way their author intended: `[JsonConstructor]` picks the constructor, and a type with a private constructor and a static
  factory (`DocsetPath.From(string)`, `OrderId.Create(Guid)`) goes through it, redrawing when its guard refuses a value. A strongly-typed id that wraps a `Guid`, `string`, `int`
  or `long` takes part in keys and foreign keys like the primitive does.
- **Unions and `[Union]` types** (C# `union Shape(Circle, Square)`, or the polyfill of it) get every case, not the one a "most parameters" rule would pick.
- **DataAnnotations** are respected: `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]`, `[Required]` and `[RegularExpression]` hold in `Plausible` data; `Mixed` and
  `Adversarial` draw each rule's **boundary** (the smallest and largest valid value, and the one just outside) as an edge.
- **At the nesting limit** a non-nullable object is its smallest valid value (collections empty, nested objects built from their own smallest parts), not `null`; only a type that
  contains itself non-nullably ends in `null`. Elements of a `List<string?>` are sometimes `null`; elements of a `List<string>` never are.
- **Foreign keys** are generated, not only valid ones:
  - Among the items of one collection: an `Id` (or `<Type>Id`) and other `*Id` members of the same type (`Person.ManagerId`) give a valid forest in `Plausible`; in
    `Mixed`/`Adversarial` a share of collections have a reference to nothing, to itself, or round a cycle of two or three.
  - **Lists of keys** (`DirectReportIds`, `IncludedIds`): a valid graph, then a dangling include, a self-include, or two items including each other.
  - **Between two collections of one object**: `Order.CustomerId` into a sibling `Customers` list (a member named `<ElementType>Id`): always valid in `Plausible`, sometimes dangling otherwise.
  - **Declared relations**, for the references the names do not give away (a list of ids on one object into a collection on another, a URI built from a repository name, a dictionary keyed by
    its entity's own name, a path that must be unique across every level of a tree). They are declared with expressions, so the member names are checked by the compiler:

    ```csharp
    GenAuto.Of<Site>(AutoMode.Mixed, o => o
        .References<Docset, Product, string>(d => d.Products, p => p.Id)                                   // list of ids -> products anywhere in the value
        .Reference<Mount, Repo, string, Uri>(m => m.Source, r => r.Name, name => new Uri($"{name}://x")) // a value built from the key
        .KeyedBy<Assembly, string, Repo>(a => a.Repositories, r => r.Name)                                 // dictionary key = entity key
        .Identity<TocNode, string>(n => n.Path));                                                          // unique across every level of a tree
    ```

    Plausible data always refers to something that exists, whichever side is generated first (a reference that comes first promises a key, and the target takes it); `Mixed` and `Adversarial`
    sometimes refer to nothing, let a dictionary key drift, or repeat a path at two levels. Types in a declared relation get four more levels before the nesting limit cuts them to a constant.
  - **Derived invariants**: a member computed from others (`PrivateRepositories` is the private subset of `AvailableRepositories`) cannot be guessed. `o.Normalize<AssemblyConfiguration>(Derive)` applies a function
    you give it, typically the loader's own derivation: always to plausible data, to about half of mixed data, never to adversarial data, so the invariant holds where it should and is still broken where a rule needs testing.
  - Not generated: references to something outside the generated value (a key into a database, a repository elsewhere). Duplicate ids and `Start`/`End` pairs are generated too.

An AutoBogus-backed generator (plausible data only, drawn under Bogus's global lock) lives in the separate, optional
[`Nullean.Entities.CsCheck.AutoBogus`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.AutoBogus) package (`GenAutoBogus.Of<T>(configure)`), for teams that already
configured `AutoFaker`. The measured case for the split is in [`docs/autobogus-decision.md`](https://github.com/nullean/entities/blob/main/docs/autobogus-decision.md).

Your own Bogus rules still work and still compose:

```csharp
Gen<OrderLine> lines = GenFakerExtensions.Faker<OrderLine>(faker => faker
    .CustomInstantiator(f => new OrderLine(f.Commerce.Ean13(), f.Random.Int(1, 100), f.Finance.Amount(0.01m, 500m))));
```

### What is routed through the PCG, and why it matters (user `Faker<T>` rules, and the AutoBogus package)

- One `Faker<T>`/`AutoFaker` per thread, created on first use and reused. Bogus takes a process-wide lock on every draw
  (`Randomizer.Locker`, an `internal static` that cannot be turned off), so a Bogus-backed generator stops scaling at about two
  threads: measured 71k `Order` draws/second on one thread and 97k on eight, against 617k and 1.6M for `GenAuto.Of<Order>()`.
  This is why `GenAuto.Of<T>` does not draw from Bogus at runtime.
- `configure` must be pure: it runs once per thread, not once per sample.
- Only draws that represent real domain data count toward `Size`; a flag, or a value that is always the same, is not recorded.
- **Routing `Random` through the PCG is not the whole story.** `Faker.IndexFaker`/`IndexVariable` are separate counters
  Bogus increments on every `Generate()`, independent of `Random`. Since the same faker is reused for every draw on a
  thread, a rule reading `f.IndexFaker` would drift across unrelated draws and silently break reproducibility. Both are
  reset to zero before every draw, at the cost of `IndexFaker` no longer being a useful sequence (it could never survive
  shrink-and-replay anyway). `f.IndexGlobal`, `f.UniqueIndex` and `Faker.GlobalUniqueIndex` are process-wide statics shared
  with every other `Faker`, so resetting them would corrupt unrelated code: avoid them in `configure` if you need
  reproducibility and shrinking.

## When a test fails, or won't run

| You see | It means | Do |
|---|---|---|
| `Set seed: "..." or -e CsCheck_Seed=...` | A real counterexample, already shrunk | Pass `seed:` (or set `CsCheck_Seed`) to replay it exactly. Iterations come from `iter:` or `CsCheck_Iter`. |
| `Requirement: X - does not hold in the state reached` with a trace | An action sequence reaches an invalid state (shortest one, for `Exhaustive`) | Fix the entity. If `Apply` is meant to stop it, add `.Enforced()`. |
| `VacuousPropertyException: ... never exercised: A:b->c` | A guard or edge was never taken: the constraint held but proved nothing there | Add an action or domain value that reaches it, or `requireCoverage: false` if it is a known gap. |
| `VacuousPropertyException: No action changed the entity` | Nothing was explored (often every action rejected, or the start already fills the limit) | Check `.Domain(...)`, `.Limit(...)`, and the start. |
| `The state space did not close` | Too many states, or states that never compare equal | Add `.Key(...)`, `.Boundary(...)`, lower `.Limit(...)`, or use `.Sample()`. |
| `AutoBogus produced no valid X ... Most rejecting constraints: A, B` | The generator cannot satisfy those constraints | Give a start (`Spec(start)`), a `gen:`, or a Bogus rule. |
| `AutoBogus could not generate a Y` / `No values to try for Y` | An action argument type AutoBogus cannot build | Declare `.Domain<Y>(...)`. |

## Links

- [Source and full documentation](https://github.com/nullean/entities); `AGENTS.md` lists what is built and what is open.
- [`AUTOTEST_NOTES.md`](https://github.com/nullean/entities/blob/main/AUTOTEST_NOTES.md): what converting the example domains found, and what did not convert.
- [CsCheck](https://github.com/AnthonyLloyd/CsCheck): the property-testing library underneath (generators, shrinking, seeds, state machines).
- [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive): the constraints this package tests.
- License: MIT
