# Nullean.Entities

You have domain rules. Right now they're scattered: a guard clause here, a FluentValidation rule
there, a hand-written unit test with one picked example, maybe a property-based test if someone on
the team likes CsCheck. Every one of those is a different predicate, rewritten by hand, for the same
underlying rule — and the rule itself has no name, so a failure just says `false`.

Nullean.Entities starts from the opposite direction: name the rule once, as a `Constraint<T>`, and
let *where you run it from* vary — a live mutation guard in production code, a one-off `Validate`
call, or thousands of CsCheck-generated scenarios trying to break it — without rewriting the rule for
any of them.

```csharp
var cancelIsIdempotent = Constraints<Order>.Idempotent(o => o.Cancel(), "Cancelling twice must leave the order unchanged.");
```

That one line is usable in three completely different contexts, unchanged: a production
`ConstraintChannel<Order>` enforcing it on every mutation, a unit test calling `.Validate(order)`
once, or a CsCheck `Sample` run checking it against hundreds of generated orders. Nothing about the
declaration knows or cares which.

## The three packages — what each is for

| Package | Reach for it when | Depends on |
|---|---|---|
| [`Nullean.Entities`](src/Nullean.Entities) | You want named, composable rules enforced at runtime — in a service, a repository, anywhere a mutation needs a guard before it commits. Works standalone; nothing here needs a test framework. | BCL only |
| [`Nullean.Entities.Reactive`](src/Nullean.Entities.Reactive) | Your rule is about a *sequence* of events over time, not one value — "never ship after cancelling," "every confirmed order closes within 30 seconds," "this eventually happens with no deadline." Lifts the same `Constraint<T>` vocabulary onto `IObservable<TEvent>`. | `Nullean.Entities` |
| [`Nullean.Entities.CsCheck`](src/Nullean.Entities.CsCheck) | You trust the rule and now want a computer trying to break it: generate hundreds of random action sequences against your SUT, shrink any failure to the smallest reproducing case, replay it from a seed. Also ships Bogus/AutoBogus-backed generators for realistic fake data. | `Nullean.Entities.Reactive`, [CsCheck](https://github.com/AnthonyLloyd/CsCheck), [Bogus](https://github.com/bchavez/Bogus) |

The three are additive, not alternatives — each one is useful alone, and each later one makes the
one before it more thoroughly exercised without asking you to re-author anything.

`Nullean.Entities` on its own: `Constraint<T>` / `ConstraintSet<T>`, with `Validate` (point-in-time),
`Apply`/`ApplyOrThrow` (safe mutation, returns `Result<T>.Ok`/`Fail` instead of throwing), and
`ConstraintChannel<T>` (holds a current value, enforces the set on every proposed change). This is
the part you could adopt even if you never write a single property-based test.

## Testing axes — independent dimensions, not stations on a line

A construct usually needs *several* of these qualities at once, not one instead of another, which
is why "axis" fits better than "vertex" (a vertex is one fixed corner of a shape; an axis is a
dimension you can add without displacing the others). They split into two families.

**Over a sequence of events, with a real or virtual clock** — needs `Nullean.Entities.Reactive`:

| Axis | The formal name, if you know one | What it actually asks |
|---|---|---|
| Absent | safety | This bad pair of events never happens (ship after cancel) |
| Temporal | bounded liveness | This good thing happens, and within a deadline |
| Liveness, unbounded | liveness | This good thing happens eventually — no deadline at all |
| Vacancy, weak | coverage | Did the generator actually reach the dangerous states, or is "Absent" only passing because nothing risky ever got generated? |
| Vacancy, proved | reachability proof | The strong form of the above: prove a state unreachable, not just "didn't see it in N runs" — needs a pure state machine, a separate design this repo hasn't built |

**Over a single operation on a value, no clock, no stream** — `Nullean.Entities` alone is enough:

| Axis | The formal name, if you know one | What it actually asks |
|---|---|---|
| Idempotency | `f(f(x)) == f(x)` | Doing it twice is the same as doing it once |
| Monotonicity | an ordering invariant | This quantity only ever moves one direction |
| Conservation | an equality invariant | This quantity is neither created nor destroyed by the transition |
| Confluence | a metamorphic property | Two paths that are supposed to agree, actually agree — the useful check when there's no independent "correct answer" to compare against |

That second family is the one worth knowing about even if you've never touched `.Reactive`: none of
idempotency, monotonicity, conservation, or confluence need an event stream. They're pure-value
properties `Nullean.Entities` expresses directly, and `Nullean.Entities.CsCheck` drives with plain
`Gen<T>.Sample` — no `Script<TSut>` required.

`Nullean.Entities.CsCheck` adds two more things once you actually want to fuzz: `Script<TSut>` to
generate, shrink, and replay whole action sequences against a real SUT, and `Metamorphic` for the
confluence axis specifically (two generated values, two paths, one equality check, on fresh
instances each time).

## Realistic fake data, without losing reproducibility

Bogus generates data that looks real — emails, addresses, product SKUs. CsCheck generates data that
shrinks and replays from a seed. Left to themselves they don't combine: Bogus has its own `Random`,
disconnected from CsCheck's, so a Bogus-backed value can't be replayed or minimized the way a
hand-written `Gen<T>` can.

`Nullean.Entities.CsCheck` bridges the two: every Bogus draw is routed through CsCheck's own `PCG`
instead of Bogus's default `Random`, and the draw count/magnitude is recorded as a CsCheck `Size` —
so a failing Bogus-generated order shrinks toward the smallest one, exactly like any other
generator.

```csharp
var genOrder = GenFakerExtensions.Faker<Order>(faker => faker
    .CustomInstantiator(f => new Order(f.Random.Guid()))
    .RuleFor(o => o.Email, f => f.Internet.Email()));

var genCustomer = GenAuto.Of<Customer>(); // AutoBogus fills every field, nested objects included
```

`Faker<T>` is for when you want to write the rules by hand; `GenAuto.Of<T>` is for when you don't
want to write any — AutoBogus fills every public member automatically. Both plug straight into a
`Script<TSut>` step's argument, so one generated action sequence can mix a Bogus-backed step with a
plain `Gen.Guid` one. See [`Nullean.Entities.CsCheck`'s README](src/Nullean.Entities.CsCheck#bogusautobogus-generators)
for the full detail on what gets routed through the `PCG` and why.

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Reactive
dotnet add package Nullean.Entities.CsCheck
```

## Building from source

Requires the .NET 11 preview SDK (pinned in `global.json`, `allowPrerelease: true`) — every project
multi-targets `net10.0;net11.0`, and a newer SDK can always build older TFMs. Expect an
`NETSDK1057` "you are using a preview version of .NET" message on every build; that is expected.

```sh
git clone https://github.com/nullean/entities.git nullean-entities
cd nullean-entities
./build.sh build
./build.sh test
```

See [AGENTS.md](AGENTS.md) for the full build target list, package layout, and design conventions.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
