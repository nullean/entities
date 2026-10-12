# Nullean.Entities

**Types say what a thing is. Nothing says what it must do.**

C# records and unions say what an order holds and which cases exist. They cannot say that a total is never negative, that cancelling twice is cancelling once, that a version never goes back, or that a confirmed order ships within a minute. Those rules end up in `if` statements, validators and people's heads, written again for each use, and they drift apart.

Nullean.Entities makes them **first-class**: a rule is a named value, declared once next to the type. The same declaration validates a value, is tested automatically on generated data, and watches live events. And the tests it writes ask nine questions most test suites never ask.

📖 **[Documentation](docs/index.md)**: [why](docs/why.md), [getting started](docs/getting-started/index.md), [the nine axes](docs/axes/index.md), [concepts in plain words](docs/concepts.md).

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

Rule broken   version-never-goes-back: Expected NonDecreasing but moved from 1 to 0.
Operation     Reset()
Started from  Order { Lines = [], Status = Cancelled, Version = 1, Total = 0, Label = "" }
Produced      Order { Lines = [], Status = Cancelled, Version = 0, Total = 0, Label = "" }

Law           No operation moves a value that may only go one way in the other direction.
Consequence   An older result overwrites a newer one, a counter goes back or a timestamp regresses, without anything saying so.

Smallest case found, after 3 shrinks. To run this case again: .Seed("2pERrTiV-JM9")
```

The example is [`examples/Nullean.Entities.Quickstart`](examples/Nullean.Entities.Quickstart), and its tests plant this bug and five more.

## The nine questions

The shape of each rule tells the library what to test. These are questions a scenario test almost never covers:

| | The question | The bug it catches |
|---|---|---|
| [01](docs/axes/01-invariant.md) | Is it true in every state? | an operation that lets a bad value in |
| [02](docs/axes/02-idempotency.md) | Does doing it twice change anything? | a retry that changes state twice |
| [03](docs/axes/03-monotonicity.md) | Does it ever go backwards? | an older result overwriting a newer one |
| [04](docs/axes/04-conservation.md) | Did anything appear or vanish? | totals that drift |
| [05](docs/axes/05-absence.md) | Can this bad sequence ever happen? | a cancelled order that still ships |
| [06](docs/axes/06-temporal.md) | Does B follow A within a deadline? | a promise missed by one unlucky gap |
| [07](docs/axes/07-liveness.md) | Does it eventually finish? | the endless spinner |
| [08](docs/axes/08-confluence.md) | Does the order inputs arrive in matter? | two rebuilds publishing different output |
| [09](docs/axes/09-vacancy.md) | Did the test even reach the rule? | a green test that checked nothing |

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
