---
navigation_title: Systems and networks
description: Model several services that talk by message, over a network that reorders, duplicates or drops, and prove the rules hold in every interleaving.
---

# Systems and networks

A single entity has one history: the actions applied to it, one after another. Two services that talk through a broker have many. Messages cross in flight, the broker redelivers, a consumer restarts halfway. Most bugs in that kind of system live in an interleaving nobody wrote a test for.

`SystemSpec` models such a system as one state: every node's state plus the messages in flight. It explores every way the network and the outside world can move it. It compiles to an `EntitySpec<SystemState>`, so `Exhaustive`, `Sample`, `Mermaid`, `Faults` and the [liveness rules](../reference/state-machines.md#liveness-something-good-eventually-happens) work unchanged.

## An outbox and a consumer

Orders publishes `OrderPlaced` through an outbox. Billing charges the order and answers `Charged`. The broker delivers at least once, so billing has to deduplicate.

```csharp
internal sealed record OrderPlaced(int OrderId);
internal sealed record Charged(int OrderId);

internal sealed record OrdersState(ImmutableSortedSet<int> Placed, ImmutableSortedSet<int> Paid);
internal sealed record BillingState(ImmutableSortedSet<int> Seen, ImmutableList<int> Charges);

var orders = SystemSpec.Node("orders", new OrdersState([], []));
var billing = SystemSpec.Node("billing", new BillingState([], []));

orders
    .Client("Place", [1], (o, id, send) =>
    {
        if (o.Placed.Contains(id)) return o;
        send(billing, new OrderPlaced(id));
        return o with { Placed = o.Placed.Add(id) };
    })
    .Handles<Charged>((o, m, _) => o with { Paid = o.Paid.Add(m.OrderId) });

billing.Handles<OrderPlaced>((b, m, send) =>
{
    if (b.Seen.Contains(m.OrderId)) return b;          // the dedupe the design relies on
    send(orders, new Charged(m.OrderId));
    return b with { Seen = b.Seen.Add(m.OrderId), Charges = b.Charges.Add(m.OrderId) };
});
```

A node is an immutable state with handlers. Declare every node first, then the handlers, so they can address each other. A handler returns the node's new state and sends through `send`. What it sends goes into flight.

The rules are ordinary constraints over `SystemState`. `node.In(state)` reads one node's state:

```csharp
var rules = ConstraintSet.From(
    ("charged-at-most-once", Constraints<SystemState>.Always(
        s => billing.In(s).Charges.GroupBy(id => id).All(g => g.Count() == 1),
        "An order is charged at most once.")));

SystemSpec.Of(orders, billing)
    .Network(Network.AtLeastOnce)
    .Spec(rules)
    .Exhaustive();
```

Remove the `Seen` check and the proof fails with the shortest interleaving that breaks it:

```text
Requirement: charged-at-most-once - does not hold in the state reached
     1 orders.Place(1)
         [orders.Place(1)] ... in flight: [orders → billing: OrderPlaced { OrderId = 1 }]
     2 Duplicate(0)
         [duplicate orders → billing: OrderPlaced { OrderId = 1 }] ...
     3 Deliver(0)
         [deliver orders → billing: OrderPlaced { OrderId = 1 }] ... billing: BillingState { Seen = [1], Charges = [1] } ...
 >>  4 Deliver(1)
         [deliver orders → billing: OrderPlaced { OrderId = 1 }] ... billing: BillingState { Seen = [1], Charges = [1, 1] } ...
```

Each state starts with what the step did, in brackets. `Deliver(0)` names a position in the list of deliverable messages, and the bracket names the message.

## What the network may do

| Network | Order | Duplicates | Losses |
|---|---|---|---|
| `Network.Reliable` (the default) | in order per sender and receiver | no | no |
| `Network.Unordered` | any | no | no |
| `Network.AtLeastOnce` | any | yes | no |
| `Network.Lossy` | any | yes | yes |

Each becomes actions: `Deliver(slot)` always, `Duplicate(slot)` when the network duplicates, `Drop(slot)` when it loses. Each `Client` on a node is an action named `node.Client`.

## Crashes

```csharp
SystemSpec.Of(orders, billing)
    .Network(Network.AtLeastOnce)
    .Crashes(billing, durable: b => b with { Seen = [] });
```

`Crashes(node, durable)` adds `Crash(node)`: the node comes back as `durable` of the state it crashed in. Messages in flight to it stay in flight, because the broker still has them. Above, billing kept its dedupe record only in memory. A crash forgets it, the redelivery is charged again, and the proof fails with `crash billing` in the trace. With `durable: b => b` (everything committed) it holds.

A crash happens between two deliveries, so a handler is atomic in the model. Whether the real handler is atomic too is a question about the code, not the design. [Conformance](#the-real-code) answers it.

## Every placed order is paid

Safety says nothing bad happens. Liveness says something good eventually does. Delivery is declared fair: a message that stays deliverable is eventually delivered. Nothing else is, since a client may stop sending and a network may keep duplicating.

```csharp
SystemSpec.Of(orders, billing)
    .Network(Network.AtLeastOnce)
    .Spec(rules)
    .LeadsTo("a-placed-order-is-paid",
        s => orders.In(s).Placed.Contains(1),
        s => orders.In(s).Paid.Contains(1))
    .Liveness();
```

On `Network.Lossy` it fails. The network may drop `OrderPlaced`, and then nothing more can happen:

```text
Liveness failed.

Rule broken   a-placed-order-is-paid: once its condition holds, every run reaches the goal.
Fair actions  Deliver

Path          orders: ... | in flight: []
            1 orders.Place(1) → ... in flight: [orders → billing: OrderPlaced { OrderId = 1 }]
            2 Drop(0) → [drop orders → billing: OrderPlaced { OrderId = 1 }] ... in flight: []

Then stops    no action changes this state, and the goal does not hold in it.
```

The fix is in the design: a retry on a timer, which is a `Client` on orders that resends what is not yet paid.

## Keeping the search finite

```csharp
.Bound(inFlight: 4, duplicates: 1, crashes: 1, collections: 4)
```

`Bound` sets these limits:

- A state with more than `inFlight` messages in flight is not expanded.
- A node state holding a collection larger than `collections` is not expanded.
- The network duplicates at most `duplicates` times per run.
- Each node crashes at most `crashes` times per run.

A proof says "nothing breaks within these bounds", the same claim `Exhaustive` makes for one entity. `Boundary(state => ...)` adds a region of your own.

### Symmetry

Orders 1 and 2 behave the same way, so exploring a state and the same state with the two swapped is wasted work. `Symmetric(ids, rename)` explores each set of renamings once:

```csharp
.Symmetric([1, 2], (s, map) => s
    .With(orders, o => new OrdersState([.. o.Placed.Select(map)], [.. o.Paid.Select(map)]))
    .With(billing, b => new BillingState([.. b.Seen.Select(map)], [.. b.Charges.Select(map)]))
    .WithMessages(m => m switch
    {
        OrderPlaced p => new OrderPlaced(map(p.OrderId)),
        Charged c => new Charged(map(c.OrderId)),
        _ => m
    }))
```

`rename` must apply the renaming everywhere an id appears: every node's state and every message. Declare only a real symmetry. If the system treats one id differently, a renaming hides states. Symmetry is sound for safety rules. `Liveness()` refuses to run under it, because a loop in the reduced graph can be one that swaps the ids on every lap.

## The real code

The model proves the design. Whether the real handlers match it is a separate question, answered by driving them on a transport the model controls. That is the next step. Until then, model a handler that is not atomic as two handlers with a state between them, and `Crashes` will find the gap.
