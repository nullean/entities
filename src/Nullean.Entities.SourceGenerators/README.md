# Nullean.Entities.SourceGenerators

The Roslyn incremental generator behind `[Constraint]`. Not installed directly — it ships inside
[`Nullean.Entities`](../Nullean.Entities)'s nupkg as an analyzer (`analyzers/dotnet/cs/`), and NuGet
carries analyzer assets transitively, so anything that depends on `Nullean.Entities` — including
`Nullean.Entities.Reactive` and `Nullean.Entities.CsCheck` — gets it too.

See [`Nullean.Entities`](../Nullean.Entities/README.md#exhaustive-constraint-sets-constraint) for
what `[Constraint]` does from a consumer's point of view. This file is implementation notes for
anyone changing the generator itself.

## What it does

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

## Why every failure mode is a refused build, not a warning

A type with any invalid `[Constraint]` member — not static, wrong type, mixed
`Constraint`/`ReactiveConstraint` kinds, not partial, nested, generic, or an inconsistent type
argument across members — gets **no generated property at all**, and every diagnostic (`NECG001`
through `NECG007` in `Diagnostics.cs`) is an error. Exhaustiveness is the entire reason
`[Constraint]` exists; silently generating from the valid subset while an invalid member doesn't
make it in would be exactly the kind of drift the attribute is supposed to prevent.

## Constraints on the implementation itself

- **netstandard2.0 only, and no `Microsoft.CodeAnalysis.CSharp.Workspaces`.** A generator runs
  inside the host compiler process; it needs the syntax/symbol APIs (`Microsoft.CodeAnalysis`,
  `Microsoft.CodeAnalysis.CSharp`), never a `Workspace` or a `Compilation` you construct yourself.
- **Grouping is by symbol, not by name.** `SymbolEqualityComparer.Default` — comparing
  `INamedTypeSymbol`s by `==`/`.Equals()` does not do what you want.
- **Generic types and nested types are refused (`NECG004`/`NECG005`), not attempted.** Neither is
  exercised by a test; both would need real design (which base-list syntax for open generics,
  which partial-declaration shape for a nested type) rather than a best-effort emit.

## Testing

`tests/Nullean.Entities.SourceGenerators.Tests` drives `CSharpGeneratorDriver` directly against
hand-built `Compilation`s — one test per generated shape and one per diagnostic. `tests/Nullean.Entities.Tests`
and `tests/Nullean.Entities.Reactive.Tests` additionally each carry one real end-to-end fixture: a
`[Constraint]`-tagged type compiled by the actual generator (wired in as an analyzer on those test
projects too, the same way it's wired into `Nullean.Entities`/`Nullean.Entities.Reactive`
themselves), asserting the generated `All` property behaves correctly at runtime — not
just that the generator emits the text you'd expect.

## Beyond constraint fields

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

