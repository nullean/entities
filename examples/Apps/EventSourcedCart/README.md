# EventSourcedCart

A shopping cart as a stream of events. `dotnet run --project examples/Apps/EventSourcedCart` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Cart.cs` | The aggregate as two **pure** functions: `Decide(state, command) -> events` and `Evolve(state, event) -> state`, and `CartRules`, what must hold of every cart however it got there. |
| `CartService.cs` | Load the stream, fold it, decide, check the rules on the cart the new events *would* give, append with an expected version. The decider is a parameter, so a test can hand in a wrong one. |
| `Program.cs` | The scenarios, narrated: replay to any point, a refused command appends nothing, a wrong decider is stopped before the stream, a stale writer is told to reload. |

Tests: `tests/Nullean.Entities.Apps.Tests/EventSourcedCart`. `EntitySpec.EventSourced(initial, Decide, Evolve, commands, CartRules.All).Spec.Exhaustive()` enumerates **every cart that can ever exist** (commands drawn from a small domain) and checks every rule on each: the rules are the assertions. A decider that forgets the per-item limit is found, with the commands that break it.

Things worth noticing:

- Because `Decide` and `Evolve` are pure, the whole model can be explored without a store, a service or a clock. That is the property to protect: keep I/O out of both.
- Two layers refuse a bad command. `Decide` refuses what is *meaningless* (a fourth apple) by producing no events. The rules refuse what is *wrong* even if the decider has a bug. The second layer is what a Marten or EventStoreDB integration would run before append (see `docs/integration.md`).
- A refusal by the decider is not an error: the cart is unchanged and nothing is appended. A violation by the rules is a `Result.Fail`.
- The rule `checked-out-is-final` is a **sequential** rule: it compares the cart before and after the events, which a plain validator on the new state cannot do.
