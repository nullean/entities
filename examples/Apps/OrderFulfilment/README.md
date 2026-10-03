# OrderFulfilment

An order service, end to end. `dotnet run --project examples/Apps/OrderFulfilment` (it exits non-zero if anything stops behaving as narrated).

It answers three questions: how a declared state machine is *implemented*, where constraints go while you write code, and how a service built on them is tested.

| File | What to copy |
|---|---|
| `Order.cs` | The lifecycle machine with a factory on every edge (`To<Placed>(_ => new Placed(0))`), bound to the aggregate (`Machine.For<Order>(...)`), and `[Constraints] OrderConstraints` with the rules about a whole order. |
| `Email.cs` | A value that cannot be invalid: private constructor, `Create` returns `Result<Email>`. |
| `OrderService.cs` | A service with no rules of its own: load, `Fire`, save, publish. Plus the DI registration. |
| `Infrastructure.cs` | The in-memory repository and publisher the app and its tests use. |
| `Program.cs` | The scenarios, narrated. |

The service tests are in `tests/Nullean.Entities.Apps.Tests/OrderFulfilment`: by example, the service **conformed to its machine** (`machine.ConformsTo`), and the rules **checked on every state it reaches** (`SampleState`). The same checks run against a `SloppyOrderService` that ships unconfirmed orders, and fail with a shrunk sequence.

Things worth noticing:

- Validation is in the factory (`Email.Create`) and in `Fire`/`Apply`, never in a constructor. A `with` copy and a deserialiser go around a constructor; they cannot go around `Apply`.
- A refused transition is a `Result.Fail` with the reason (`'Confirm' is not permitted for this Placed: its guard rejected it`), not an exception, and nothing is saved or published.
- `Placed(int Lines)` carries data only a placed order has, and the guard `p => p.Lines > 0` reads it. The machine rejects `Draft -> Confirmed` structurally, before any guard runs.
