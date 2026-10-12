# Nullean.Entities

Declare each rule of your domain once. The library picks the test, runs it on generated data, and fails if the test never reached the rule.

C# records and unions say what shape your domain has. They cannot say that a total is never negative, that cancelling twice is cancelling once, that a version never goes back, or that a confirmed order ships within a minute. Those rules end up scattered across services, validators and tests, written again for each use, and they drift apart.

📖 **[Documentation](docs/index.md)**: [getting started](docs/getting-started/index.md), [the bugs you did not write a test for](docs/bugs/index.md), [design principles](docs/design-principles/index.md).

## Why

**Rules in one place.** A rule is a named value next to the type. `[Constraints]` gathers them into one set, so a rule you add cannot be forgotten.

**Tests you do not write.** The shape of a rule picks the test: `Idempotent` runs the action twice, `Monotonic` runs the operations and compares before and after, a deadline rule is tried with delays either side of the deadline.

**A test that proves it ran.** If the generated data never reached a rule, the test fails as vacuous instead of passing quietly. A failure is the smallest case that breaks the rule, and a seed that replays it.

**A way in for existing code.** Rules start as warnings and become errors on a date. See [adopting in an existing codebase](docs/guides/adopting.md).

## An order, in one screen

```csharp
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}
```

```csharp
AutoTest.For(OrderConstraints.Instance, orders).Run();   // one test per axis the rules declare
```

When a method of `Order` resets the version, you read:

```text
Monotonicity failed.
Law: No operation moves a value that may only go one way in the other direction.
Consequence: An older result overwrites a newer one, a counter goes back or a timestamp regresses, without anything saying so.
Set seed: "cghvRTcP33n7" or -e CsCheck_Seed=cghvRTcP33n7 to reproduce (3 shrinks, 28 skipped, 200 total).
Order { Lines = [], Status = Cancelled, Version = 1, Total = 0, Label = "" } | Reset()
Reset() from Order { ... Version = 1 ... } gave Order { ... Version = 0 ... }, which violates: version-never-goes-back: Expected NonDecreasing but moved from 1 to 0.
```

The example is [`examples/Nullean.Entities.Quickstart`](examples/Nullean.Entities.Quickstart), and its tests plant this bug and five more.

## The nine axes

Every rule is about one state, two states, or a sequence of events. These are the nine tests that follow.

| Rule is about | Axis | The question | You declare |
|---|---|---|---|
| One state | 01 Invariant | Is it true in every state? | `Always`, `When…Requires`, `Each`, `Unique`, … |
| Two states | 02 Idempotency | Twice = once? | `Idempotent(f)` |
| | 03 Monotonicity | Does it go backwards? | `Monotonic`, `MonotonicPerKey` |
| | 04 Conservation | Did anything appear or vanish? | `Conserves(select)` |
| Events over time | 05 Absence | Can this sequence happen? | `Temporal<T>.Never`, `Always`, `Transitions` |
| | 06 Temporal | Does B follow A within a deadline? | `After().Then().Within()` |
| | 07 Liveness | Does it eventually finish? | `Temporal<T>.Eventually` |
| Arrival order | 08 Confluence | Does the order inputs arrive in matter? | `OrderIndependent` |
| The test itself | 09 Vacancy | Did the test reach the rule? | nothing: automatic |

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.CsCheck   # tests only
```

The packages are not published yet; until they are, reference them from a checkout.

| Package | What it adds |
|---|---|
| `Nullean.Entities` | `Constraint<T>`, `[Constraints]`, `Temporal<T>` rules, state machines. No other dependency. |
| `Nullean.Entities.CsCheck` | `AutoTest`, `EventAutoTest`, `Script` on a `TimeProvider`, generated data. Tests only. |
| `Nullean.Entities.CsCheck.Reactive` | *Optional.* The same scripts on an Rx scheduler. |
| `Nullean.Entities.Reactive` | *Optional.* Constraints over `IObservable<T>`. |
| `Nullean.Entities.FileSystem`, `.CsCheck.FileSystem` | *Optional.* Rules over files and generated file-system worlds. |
| `Nullean.Entities.Extensions.DependencyInjection`, `.AspNetCore`, `.Argh` | *Optional.* Validate options, requests and command-line arguments. |
| `Nullean.Entities.CsCheck.AutoBogus` | *Optional.* Real-world data from an AutoFaker. |

## Building from source

Requires the .NET 11 preview SDK (pinned in `global.json`); every project multi-targets `net10.0;net11.0`.

```sh
./build.sh build
./build.sh test
```

See [AGENTS.md](AGENTS.md) for the build targets, package layout and conventions.

## License

MIT, see [LICENSE.txt](LICENSE.txt).
