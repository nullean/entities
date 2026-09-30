# Nullean.Entities.SourceGenerators

The Roslyn incremental generator behind `[Constraint]`. Not installed directly — it ships inside
[`Nullean.Entities`](../Nullean.Entities)'s nupkg as an analyzer (`analyzers/dotnet/cs/`), and NuGet
carries analyzer assets transitively, so anything that depends on `Nullean.Entities` — including
`Nullean.Entities.Reactive` and `Nullean.Entities.CsCheck` — gets it too.

See [`Nullean.Entities`](../Nullean.Entities/README.md#exhaustive-constraint-sets-constraint) for
what `[Constraint]` does from a consumer's point of view. This file is implementation notes for
anyone changing the generator itself.

## What it does

Finds every static field/property tagged `[Constraint]`, groups them by containing type, and
emits one `Constraints` property per group:

- `Constraint<T>` members where the containing type **is** `T` — the domain type declares its own
  rules — get `IHaveConstraints<T>` added to the same partial declaration too, so
  `events.AttachConstraints()` (`Nullean.Entities.Reactive`) just works.
- `Constraint<T>` members on any other partial type (a companion "spec" class) get the same plain
  `ConstraintSet<T> Constraints` property with no interface — for a `T` you don't own, or when you
  simply prefer rules declared alongside the type rather than on it. Both cases build the property
  from `ConstraintSet.From((nameof(field), field), ...)`.
- `ReactiveConstraint<T>` members get a `ReactiveConstraintSet<T> Constraints` property built from
  `ReactiveConstraintSet.From((nameof(field), field), ...)`. There's no reactive equivalent of
  `IHaveConstraints<T>`, so this case never adds an interface.

Both kinds name every member from its own field/property name rather than a string literal typed
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
themselves), asserting the generated `Constraints` property behaves correctly at runtime — not
just that the generator emits the text you'd expect.
