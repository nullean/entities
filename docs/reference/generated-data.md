---
navigation_title: "Generated data"
description: "`GenAuto`, plausible and hostile data, boundaries, AutoBogus, and why every draw goes through the PCG."
---

# Generated data

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
- **Internal construction** (a domain that keeps production code from building or editing its records): a type with no public constructor but an `internal` one is built through it, and a
  property with an `internal` (or `internal init`) setter is filled like a public one. The generator reaches them by reflection, which does not need `InternalsVisibleTo`; the test code that
  then writes `with { ... }` does. A `private` or `protected` setter is still left alone.
- **Not built by AutoGen:** classes with neither a public or internal constructor nor a static `From/`Create`/`Of`/`New`/`Parse` factory. Everything else is: immutable, frozen and read-only collections, `System.Tuple`, `KeyValuePair`, value tuples,
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
  - **Relations declared once, as rules.** A relation can also be declared as the rule itself, so the rule and the generator cannot drift apart. `Relation.Reference`, `Relation.ReferenceList` and `Relation.Unique` build the rule, and
    `o.From(rules)` (applied for you by `AutoTest.For(rules)`) reads what each one relates and declares it for the generator:

    ```csharp
    public static readonly Constraint<DocsetInCodex> CodexGroupKnown = Relation
        .Reference((DocsetInCodex p) => new[] { p.Docset }, d => d.CodexGroup, p => p.Codex.Groups, g => g.Id)
        .Rule(message: "codex.group names a group the codex site does not define.");

    DomainGen.Of<DocsetInCodex>(o => o.From(CodexConstraints.All));   // no .Reference<...>() to keep in step
    ```

    The rule runs the delegates you pass; nothing is compiled, so this is safe in a native AOT build. The member names the generator needs are read once from the text of the `from` and `key` lambdas, so they must be plain member
    accesses (`d => d.CodexGroup`). Anything else (`d => d.Name.ToLower()`, a conditional) cannot be read and `From` throws saying which, rather than quietly drawing data that ignores the relation. A rule whose projections are not
    members (a scheme read from a URI, the keys of a dictionary) stays a `Constraints<T>.References` rule with a hand-declared relation. Where the target type also appears outside `targets`, a generated reference can point at one of those, so scope the
    relation to a type that only appears where the rule looks.
  - **Derived invariants**: a member computed from others (`PrivateRepositories` is the private subset of `AvailableRepositories`) cannot be guessed. `o.Normalize<AssemblyConfiguration>(Derive)` applies a function
    you give it, typically the loader's own derivation: always to plausible data, to about half of mixed data, never to adversarial data, so the invariant holds where it should and is still broken where a rule needs testing.
    - **Size**: collections get `MaxCollection` scaled across the distribution (it now has an effect above 4) and narrow by one at each level of nesting, so a tree stays a few dozen nodes however deep it goes. `o.Large()` is the preset for
    realistic trees (`MaxDepth` 6, up to eight per collection); the defaults keep property tests quick. `MaxDepth` counts a collection and then its element as two levels, so a tree of N levels of nodes needs a `MaxDepth` of about 2N:
    a rule about a chain of six (`Probe` reports it as never violated) needs `MaxDepth = 16`, not 6.
    Every count from none to `MaxCollection` can occur, so a failure at exactly four items is as reachable at `MaxCollection` 12 as at 4, and it shrinks to four. Sizes put structure first (items before magnitudes), which is what shrinks a
    failure to its fewest items. A collection that plans at least `EarlyExitFrom` (32) items stops drawing while CsCheck is shrinking once it can no longer beat the smallest failure so far: automatic for big values, a shrink of a
    400-item value takes about a millisecond instead of 25, and smaller values are generated exactly as without it (`0` applies it everywhere, `int.MaxValue` never).
  - **`Probe` says how big the values were.** `ProbeReport.Reach` (`LargestCollection`, `Depth`) and a line under the table: "The generated values went up to N items in a collection and D levels deep", with a reminder to raise `MaxCollection` or
    `MaxDepth` when a rule is never violated. A rule about more than that cannot fail on values that never had it, which is a limit of the generator and not a tautology.
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
