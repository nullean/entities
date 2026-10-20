# Nullean.Entities.SourceGenerators

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.svg)](https://www.nuget.org/packages/Nullean.Entities)

**Declare a rule once and the set of rules for a type builds itself: add a field and it is checked, delete it and it is gone, and a mistake fails the build instead of dropping a rule.**

Without a generator, a `ConstraintSet<T>` is a hand-kept list of the rules a type declares, and a rule added to the type but not to the list silently never runs.
This Roslyn incremental generator reads the rules you declare (`[Constraints]`, `[Constraint]`) and the DataAnnotations already on your model, and emits the `ConstraintSet<T>` for them, named after the members.
It is not a separate NuGet package: it ships inside [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities) as an analyzer. The code it emits is plain C# with no reflection, so it is trim and AOT clean.

## Install

You do not install it. Add `Nullean.Entities` (targets `net10.0` and `net11.0`) and the generator comes with it:

```bash
dotnet add package Nullean.Entities
```

How it is referenced, from the csproj files:

- `Nullean.Entities.SourceGenerators` is `netstandard2.0` and `IsPackable=false`: it is never published on its own.
- `Nullean.Entities.csproj` references it with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` and packs its DLL under `analyzers/dotnet/cs`. The generator therefore never appears in the package's dependency list.
- NuGet analyzer assets flow transitively through `PackageReference`, so a project that references `Nullean.Entities.Reactive` or `Nullean.Entities.CsCheck` gets the generator too.

## Get going

Mark a `partial` type with `[Constraints]` and declare static rules on it:

```csharp
using Nullean.Entities;

public sealed record Order(Guid Id, decimal Total);

[Constraints<Order>]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");
	public static readonly Constraint<Order> TotalIsBounded = Constraints<Order>.Always(o => o.Total <= 1_000_000, "An order total is bounded.");
}

foreach (var v in OrderConstraints.All.Validate(new Order(Guid.NewGuid(), -5)))
	Console.WriteLine($"{v.ConstraintId}: {v.Message}");
```

```text
total-never-negative: An order total is never negative.
```

The generator emitted `OrderConstraints.All` (a `ConstraintSet<Order>`) from both fields. The id of each rule is the kebab-case of its member name (`TotalNeverNegative` becomes `total-never-negative`). A rule added later is in `All` on the next build.

The second use is DataAnnotations. A type with `[Constraints]` gets one rule for each `[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]` and `[RegularExpression]`, with no opt-in:

```csharp
using System.ComponentModel.DataAnnotations;
using Nullean.Entities;

[Constraints]
public sealed partial record SignUp([Required] string Name, [Range(18, 120)] int Age, [RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference);

foreach (var v in SignUp.Constraints.Validate(new SignUp("Ada", 12, "ABC-123")))
	Console.WriteLine($"{v.ConstraintId} [{v.Member}]: {v.Message}");
```

```text
age-range [Age]: The field Age must be between 18 and 120.
```

A mistake is a build error, with nothing generated for that type. A `[Constraints]` class that is not `partial`:

```text
error NECG001: 'ThingRules' has [Constraint] members but is not declared partial; the generator cannot add a Constraints property to it
```

## What you get

| You write | The generator emits |
|---|---|
| `[Constraints]` on a type with static `Constraint<T>` members | `Type.All`, a `ConstraintSet<T>` of every such member. |
| `[Constraint]` on a static member | The same, for tagged members only. On the type that is `T` it is named `Constraints` and the type also gets `IHaveConstraints<T>`. |
| `[Constraints<T>]` on a companion class | A set for `T` from its annotations, plus `Instance` and `Actions` (the operations of `T`, as data), even with no hand-written rule. |
| `Constraint<T>` members on a companion type | Also a nested `TypeConstraints` (an `IConstraintsFor<T>` struct) and a forwarding `Instance`. |
| `ReactiveConstraint<T>` members | A `ReactiveConstraintSet<T> All` with `IReactiveConstraintsFor<T>`; see [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive). |
| DataAnnotations on `T` | Rules named `<property>-<attribute>`, such as `name-required` and `age-range`. |
| A `required` or get-only non-nullable property | A null-only `<property>-required` rule (off with `ImplicitRequired = false`). |
| `[ConstraintIgnore]` | Leaves a member, property or method out. |

Diagnostics: `NECG001` to `NECG007` are errors (not partial, not static, wrong type, a non-partial container, mixed kinds, inconsistent type argument, a member about another type). `NECG010` (an unsupported validation attribute) and `NECG020` (a method not used as an action) are Info.

## Pitfalls and limits

- The generator is `netstandard2.0` and runs in the compiler process. It never reflects over your assemblies at runtime; the code it emits is plain C#.
- A type with any invalid member gets no generated property at all. This is deliberate: a set built from the valid subset would hide the member that was left out.
- Types must be `partial`, and so must every type that contains them. Nested and generic types are supported.
- C# does not allow a type parameter in an attribute argument, so a generic companion declares a `Constraint<Box<T>>` field instead of using `[Constraints<Box<T>>]`.
- Adding `required` or get-only members to a type under `[Constraints]` adds implicit `-required` rules to its generated set.
- A validation attribute the generator does not know (`[Phone]`, a custom one, `IValidatableObject`) is reported as `NECG010`, not turned into a rule.
- Annotations cover one property at a time. Cross-field rules, collections and sequential rules are `Constraint<T>` members.

## Next

- [Nullean.Entities on NuGet](https://www.nuget.org/packages/Nullean.Entities): the package that carries this generator, with the full API.
- [Getting started](https://entities.nullean.net/getting-started/)
- [Known limitations](https://entities.nullean.net/known-limitations/)
- Generator source: https://github.com/nullean/entities/tree/main/src/Nullean.Entities.SourceGenerators
- License: MIT

## Implementation notes

The rest of this file is for anyone changing the generator itself.

### How it discovers members

Two independent discovery pipelines feed the same grouping/validation/emission path:

- Every static field/property tagged `[Constraint]` (explicit, per-member).
- Every static `Constraint<T>`/`ReactiveConstraint<TEvent>`-typed member declared directly on a
  type tagged `[Constraints]` (implicit, per-type) — found by walking the type's own members with
  `INamedTypeSymbol.GetMembers()`, skipping anything also tagged `[ConstraintIgnore]`. A type using
  `[Constraints]` has any of its own `[Constraint]`-tagged members merged away as redundant rather
  than counted twice: `Merge` drops every explicit member whose containing type was also seen by
  the `[Constraints]` pipeline, before the two lists are concatenated. One accepted consequence —
  a member on a `[Constraints]`-tagged type that is wrongly `[Constraint]`-tagged *and* the wrong
  type is silently dropped by that same filter, with no diagnostic, since it was never going to be
  auto-discovered either way and `[Constraint]` is simply inert once `[Constraints]` is present.

The merged member list groups by containing type, and emits one `All` property per group:

- `Constraint<T>` members where the containing type **is** `T` — the domain type declares its own
  rules — get `IHaveConstraints<T>` (F-bounded, static-abstract) added to the same partial
  declaration too, so `events.AttachConstraints()` (`Nullean.Entities.Reactive`) just works.
- `Constraint<T>` members on any other partial type (a companion "spec" class) get the same plain
  `ConstraintSet<T> All` property, plus a nested `TypeConstraints` struct implementing
  `IConstraintsFor<T>` — a plain *instance* interface, unlike `IHaveConstraints<T>`, so
  `SpecClass.TypeConstraints.Instance` is a value generic code can take as a parameter (inferring
  `T` from it) rather than only a type to constrain on. It's nested, never on the spec class's own
  base list, because `SpecClass.All` is `static` regardless of whether the class itself is
  — there's nothing on the class that could implement an instance interface member. A forwarding
  `SpecClass.Instance` field on the outer class itself means a caller never has to spell out
  `TypeConstraints`. Both cases build the property from `ConstraintSet.From((ConstraintName.Of(nameof(field)), field), ...)`; the id is the kebab-case of the member name (`PathPrefixesUnique` becomes `path-prefixes-unique`).
- `ReactiveConstraint<T>` members get a `ReactiveConstraintSet<T> All` property with the
  same nested `TypeConstraints : IReactiveConstraintsFor<T>` treatment, built from
  `ReactiveConstraintSet.From((ConstraintName.Of(nameof(field)), field), ...)`. There's no reactive-native self-attach
  interface to choose instead — self-attach for reactive flows through the sync core's
  `IHaveConstraints<T>` + `ToReactive()` (see `AttachConstraints` in `Nullean.Entities.Reactive`) —
  so every declarer goes through this one path.

All three name every member from its own field/property name rather than a string literal typed
at the declaration site — a constraint's name can never drift from the field that declares it.

### Why every failure mode is a refused build, not a warning

A type with any invalid `[Constraint]` member — not static, wrong type, mixed
`Constraint`/`ReactiveConstraint` kinds, not partial (or nested in something that is not), or an inconsistent type
argument across members — gets **no generated property at all**, and every diagnostic (`NECG001`
through `NECG007` in `Diagnostics.cs`) is an error. Exhaustiveness is the entire reason
`[Constraint]` exists; silently generating from the valid subset while an invalid member doesn't
make it in would be exactly the kind of drift the attribute is supposed to prevent.

### Constraints on the implementation itself

- **netstandard2.0 only, and no `Microsoft.CodeAnalysis.CSharp.Workspaces`.** A generator runs
  inside the host compiler process; it needs the syntax/symbol APIs (`Microsoft.CodeAnalysis`,
  `Microsoft.CodeAnalysis.CSharp`), never a `Workspace` or a `Compilation` you construct yourself.
- **Grouping is by symbol, not by name.** `SymbolEqualityComparer.Default` — comparing
  `INamedTypeSymbol`s by `==`/`.Equals()` does not do what you want.
- **Generic and nested types are supported, with one rule.** Every containing type must be `partial` (`NECG005` otherwise); the generator re-declares them and carries type parameters into the generated declaration.

### Testing

`tests/Nullean.Entities.SourceGenerators.Tests` drives `CSharpGeneratorDriver` directly against
hand-built `Compilation`s — one test per generated shape and one per diagnostic. `tests/Nullean.Entities.Tests`
and `tests/Nullean.Entities.Reactive.Tests` additionally each carry one real end-to-end fixture: a
`[Constraint]`-tagged type compiled by the actual generator (wired in as an analyzer on those test
projects too, the same way it's wired into `Nullean.Entities`/`Nullean.Entities.Reactive`
themselves), asserting the generated `All` property behaves correctly at runtime — not
just that the generator emits the text you'd expect.

### Beyond constraint fields

- **DataAnnotations** on the constrained type (`[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]`, `[RegularExpression]`) become generated constraints named kebab-case `<property>-<attribute>` (`name-required`, `age-range`), with no opt-in. Any other
  validation attribute gets the Info diagnostic `NECG010`; `[ConstraintIgnore]` on a property leaves its annotations out.
- **Members that must be set** (a non-nullable reference property declared `required`, or a get-only auto property) also get a null-only `<property>-required` rule unless they carry an explicit `[Required]`
  (then that is the one rule) or `[ConstraintIgnore]`. Value types, `Nullable<T>` and `T?` references get none; positional record parameters are init properties, so none either. Opt out with
  `ImplicitRequired = false` on `[Constraints]` / `[Constraints<T>]`. This adds rules to the generated set of existing types that use `required` or get-only members.
  - **They are prerequisites.** When a `<property>-required` rule is violated, `Validate`, `Report` and the checks on a change report only those rules and skip the rest: the other rules were written for a value that has the
    member, so a rule that reads it (`Name.Contains('/')`) would throw a `NullReferenceException` and hide every other finding. Mark a rule of your own with `.AsPrerequisite()` for the same effect
    (`Constraint<T>.IsPrerequisite`). An explicit `[Required]` is not one: it also rejects an empty string, which the other rules can read.
  - **They reach into what the type holds.** The members that must be set on a nested object, on the items of a list or on the values of a dictionary get a rule too, named after the path
    (`environments-uri-required`, `owner-address-city-required`), reached null-safely (an absent holder says nothing about its members) up to three levels deep. The nested type needs no attribute.
    `ImplicitRequired = false` turns these off with the others.
- **`[Constraints<T>]`** on a companion class names the type its rules are about, so a companion with no hand-written rule still gets `T`'s annotations (and `T`'s `Actions`). A member about another type is `NECG007`.
- **`Actions`** lists the entity's operations as data (`EntityActions<T>`), for test drivers that walk its states or run its operations. A public instance method with zero or one by-value argument is an action when it yields the next `T`:
  it returns `T`, or a **generic wrapper of `T`** (a `Result<T>` that carries diagnostics, a tuple) that has exactly one readable member of type `T`, which the action reads (`x.Close().Value`). With several members of type `T`
  the one named `Value`, `Result`, `Next`, `Current`, `Entity` or `Attempted` is read; `[ConstraintAction(nameof(Member))]` on the method names the member when that is not what you want. A wrapper that is not generic over `T` needs
  the attribute (reading "any member of type `T` on any return type" would mistake a `Person` with a `Ticket` property for an operation on the ticket). `[ConstraintIgnore]` excludes a method.
  A method that looks like an operation but cannot be used is skipped with an **info** diagnostic (`NECG020`) saying why, so nothing is passed over in silence: it is asynchronous (`Task<T>`, an action is a synchronous function),
  it is a Try-method with an `out T`, or its wrapper has no readable `T` or several with no conventional name. A method that has nothing to do with `T` (returns `bool`, a `string`) is not reported.
- **Nested and generic types** are supported: every containing type must be `partial` (otherwise `NECG005`), and type parameters are carried into the generated declaration. C# does not allow a type parameter in an attribute's
  type argument, so a generic companion declares a `Constraint<Box<T>>` field instead of using `[Constraints<Box<T>>]`.

