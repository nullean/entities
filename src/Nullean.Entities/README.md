# Nullean.Entities

Rules you write once and the compiler enforces everywhere — for the parts of your domain that guards
and `if` statements let slip through.

## The problem with guards

A guard in a service method enforces a rule at one call site. Add a second path into the same
operation and the guard has to be copied. Forget to copy it and nothing fails at compile time — the
rule just silently stops applying.

```csharp
// Before: a rule written three ways, in three places, drifting apart
public void Confirm(Order order)
{
    if (order.Lines.Count == 0)
        throw new InvalidOperationException("Cannot confirm an empty order.");
    // ...
}

public async Task ConfirmAsync(Order order)
{
    if (order.Lines.Count == 0)           // same rule, copy-pasted
        throw new InvalidOperationException("Cannot confirm an empty order.");
    // ...
}

public void BulkConfirm(IEnumerable<Order> orders)
{
    foreach (var o in orders.Where(o => o.Lines.Count > 0)) // different expression, same intent
    {
        // ...
    }
}
```

```csharp
// After: one rule, applied at every call site
[Constraints]
public static partial class OrderConstraints
{
    public static readonly Constraint<Order> MustHaveLines =
        Constraints<Order>.Always(o => o.Lines.Count > 0, "An order must have at least one line.");

    public static readonly Constraint<Order> LegalStatusTransitions = Constraints<Order>
        .Transitions(o => o.Status)
        .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
        .Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
        .Allow(OrderStatus.Pending, OrderStatus.Cancelled)
        .Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);
}

// Every caller uses the same set — the rule cannot drift
var result = OrderConstraints.All.Apply(order, o => o.Confirm());
```

`[Constraints]` auto-discovers every `Constraint<Order>` field on `OrderConstraints` and generates
`OrderConstraints.All` — a `ConstraintSet<Order>`. Add a new field and it's included on the next
build; remove one and it disappears. No list to maintain, no drift between field name and constraint
name.

## What Apply returns

`ConstraintSet<T>.Apply` takes the current value and a mutation, runs every constraint against the
result, and returns a three-way union:

```csharp
var result = OrderConstraints.All.Apply(order, o => o.Confirm());

switch (result)
{
    case ChannelResult<Order>.Applied(var next):
        // mutation passed — next is the new value
        break;
    case ChannelResult<Order>.Noop(var current):
        // mutation produced the same value — nothing ran
        break;
    case ChannelResult<Order>.Failure(var current, var violations):
        // one or more constraints rejected the mutation
        foreach (var v in violations)
            logger.LogWarning("{Rule}: {Message}", v.ConstraintId, v.Message);
        break;
}
```

`Noop` short-circuits without running any constraints when the mutation produces an equal value —
cancelling an already-cancelled order is free. `Applied` means every constraint passed. `Failure`
means at least one rejected it, and you have the full violation list with names, messages, and
evidence attached.

`ApplyOrThrow` is the same thing without the switch, for cases where a violation is truly exceptional.

## Long-lived objects: ConstraintChannel

For objects that evolve over time — an order being processed, a room being booked — a
`ConstraintChannel<T>` wraps the current value and applies constraints on every mutation, without the
caller re-plumbing the constraint set each time:

```csharp
var channel = ConstraintChannel
    .For(order)
    .Enforce(OrderConstraints.All);

channel.Apply(o => o.Confirm());   // Applied — channel.Current is now Confirmed
channel.Apply(o => o.Confirm());   // Noop — already Confirmed, nothing ran
channel.Apply(o => o.Ship());      // Applied — channel.Current is now Shipped
channel.Apply(o => o.Cancel());    // Failure — can't cancel a shipped order
```

## What you can express

Every constraint is a named, quoted value — not an anonymous lambda hidden inside a method.

```csharp
[Constraints]
public static partial class OrderConstraints
{
    // A predicate that must always hold, checked on every Validate or Apply
    public static readonly Constraint<Order> TotalNeverNegative =
        Constraints<Order>.Always(o => o.Total >= 0, "Order total must not go negative.");

    // A rule whose message is built from the value itself
    public static readonly Constraint<Order> LineSkusMustBeUnique = Constraints<Order>.Check(o =>
        o.Lines
            .GroupBy(l => l.Sku)
            .Where(g => g.Count() > 1)
            .Select(g => Violation.Error($"Duplicate SKU '{g.Key}' — each line must be unique."))
            .ToList()
    );

    // A conditional rule: when the guard holds, the requirement must too
    public static readonly Constraint<Order> ConfirmedOrderRequiresPaymentRef = Constraints<Order>
        .When(o => o.Status == OrderStatus.Confirmed)
        .Requires(o => o.PaymentRef is not null, "A confirmed order must have a payment reference.");

    // Legal state transitions — the allow-list is the spec, not a switch expression
    public static readonly Constraint<Order> LegalStatusTransitions = Constraints<Order>
        .Transitions(o => o.Status)
        .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
        .Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
        .Allow(OrderStatus.Pending, OrderStatus.Cancelled)
        .Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);

    // f(f(x)) == f(x) — applied once or twice gives the same result
    public static readonly Constraint<Order> CancelIsIdempotent =
        Constraints<Order>.Idempotent(o => o.Cancel(), "Cancelling twice must leave the order unchanged.");

    // A selected value never moves the wrong way across a specific mutation
    // Sequential — only fires from Apply, not Validate. Attach to the step set that owns AddLine.
    public static readonly Constraint<Order> TotalNeverDecreasesOnAddLine =
        Constraints<Order>.Monotonic(o => o.Total, MonotonicDirection.NonDecreasing);

    // A quantity conserved across a pair of values — no value is created or destroyed
    public static readonly Constraint<(Order From, Order To)> LineTransferConservesTotal =
        Constraints<(Order, Order)>.Conserves(p => p.From.Total + p.To.Total);
}
```

`Transitions`, `Monotonic`, and `Conserves` are *sequential* — they compare two values, so they only
fire from `Apply`, never from `Validate`. A violation says "Monotonic was violated" or "Conserves was
violated", not just "a predicate failed" — the failure names the quality that broke.

## Declared state machines

For domains where each state is its own shape — not just an enum value but a type carrying data —
`StateMachine<TState>` declares the graph once and validates it eagerly at startup:

```csharp
private abstract record OrderState
{
    public sealed record Draft : OrderState;
    public sealed record Placed(int Lines) : OrderState;
    public sealed record Confirmed : OrderState;
    public sealed record Shipped : OrderState;
    public sealed record Cancelled : OrderState;
}

public static readonly StateMachine<OrderState> OrderLifecycle = StateMachine<OrderState>.Define(m => m
    .Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>())
    .State<OrderState.Placed>(s => s
        .On("Confirm").To<OrderState.Confirmed>(when: p => p.Lines > 0)
        .On("Cancel").To<OrderState.Cancelled>())
    .State<OrderState.Confirmed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
    .Terminal<OrderState.Shipped>()
    .Terminal<OrderState.Cancelled>()
);
```

`Define` throws `StateMachineDefinitionException` (NESM001–NESM004) if the graph fails exhaustiveness,
reachability, dead-end, or trap checks — caught at startup, not buried in a log at runtime.

From a defined machine you get:

```csharp
OrderLifecycle.AsConstraint()  // Constraint<OrderState> — drops into ConstraintSet.From(...)
OrderLifecycle.AsReactive()    // ReactiveConstraint<OrderState> — checks an event stream
OrderLifecycle.ToMermaid()     // Mermaid diagram — paste into your README or docs
OrderLifecycle.Walks()         // Gen<IReadOnlyList<StateMachineStep<T>>> — CsCheck graph walks
OrderLifecycle.RealizedWalks() // Gen<IReadOnlyList<RealizedStep<T>>> — guard-aware walks with real values
```

For enum-shaped state machines, `Constraints<T>.Transitions(selector).Allow(from, to)` stays the right
tool — `StateMachine<TState>` is for the case where each state carries its own data and a guard needs
to inspect it.

## Validating without mutating

`ConstraintSet<T>.Validate` runs only the point-in-time constraints (anything that isn't `Sequential`)
and returns the full violation list:

```csharp
IReadOnlyList<Violation> violations = OrderConstraints.All.Validate(order);
if (violations.Count > 0)
    return BadRequest(violations.Select(v => v.Message));
```

`Validate` is the right tool for an incoming API payload before you ever create a mutation. `Apply` is
the right tool for a proposed change — it runs both point-in-time *and* sequential constraints.

## The source generator

`[Constraints]` on a companion class is the most common pattern:

```csharp
[Constraints]
public static partial class SupportTicketConstraints
{
    public static readonly Constraint<SupportTicket> LegalStatusTransitions = Constraints<SupportTicket>
        .Transitions(t => t.Status)
        .Allow(TicketStatus.Open, TicketStatus.InProgress)
        .Allow(TicketStatus.InProgress, TicketStatus.Resolved)
        .Allow(TicketStatus.Resolved, TicketStatus.Closed)
        .Allow(TicketStatus.Resolved, TicketStatus.InProgress);

    public static readonly Constraint<SupportTicket> ResolvedRequiresTimestamp = Constraints<SupportTicket>
        .When(t => t.Status is TicketStatus.Resolved or TicketStatus.Closed)
        .Requires(t => t.ResolvedAt is not null, "Resolved/closed tickets must have a resolution timestamp.");
}

// Generated: public static ConstraintSet<SupportTicket> All { get; }
```

`[ConstraintIgnore]` on a field opts it out — for a building-block constraint that composes into
others but shouldn't be counted as one of the type's own rules. A misused `[Constraint]` member (not
static, wrong type, not partial) refuses to generate anything for that type and emits a build error —
the generator never silently generates from a partial subset.

See [`Nullean.Entities.SourceGenerators`](../Nullean.Entities.SourceGenerators/README.md) for the full
diagnostic list.

## Install

```sh
dotnet add package Nullean.Entities
```

The source generator ships inside this package's nupkg as an analyzer — no separate install.

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — temporal and
  structural constraints over `IObservable<TEvent>` streams
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck) — all eight
  CsCheck testing axes, driven by named constraint sets and shrinkable Bogus generators
- License: MIT
