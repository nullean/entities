---
navigation_title: Rules over real models
description: Rules over two aggregates, the layer a rule belongs on, and severities that notice without rejecting.
---

# Rules over real models

## Rules over two aggregates: the pair model

A rule that needs two aggregates (a documentation set and the product catalog its ids point into, a navigation and the assembly whose repositories it mounts) is a constraint over a
record that holds both. docs-builder's own `DocumentationSetInCatalog(Docset, ProductCatalog)` is exactly this, and the shapes work on it unchanged:

```csharp
public sealed record DocsetInCatalog(Docset Docset, Catalog Catalog);

public static readonly Constraint<DocsetInCatalog> ProductsExist = Constraints<DocsetInCatalog>
    .References<string, string>(p => p.Docset.Products, id => [id], p => p.Catalog.Products.Select(x => x.Id));
```

To reuse rules written for one half, `ConstraintSet.Pair(setForFirst, setForSecond)` makes one set over `(First, Second)` that keeps each rule's name, and `ConstraintSet.Merge(a, b)` joins sets over one type.
Give the pair only what its rules need: the existing pair models carry only the product catalog, which is why a codex group, a cross-link repository or a mount's repository was never checked.

How the references are written does not matter to the rules: a list of id strings, a key in a dictionary, the scheme or path of a `Uri`, a path inside a tree. `References` takes any projection,
`KeysMatch` covers a dictionary keyed by the entity's own name, and `Tree.Flatten(roots, n => n.Children)` lets `Unique`, `References` and `Acyclic` run over every level of a tree at once.
For generated data, declare those relations to AutoGen (`o.References<Docset, Product, string>(d => d.Products, p => p.Id)`, see the CsCheck README): the names alone will not give them away.

**Where a violation points.** `Unique`, `References`, `Acyclic` and `NoChains` set `Violation.Member` from the text of the collection selector, not from the key: `s => s.Orders` gives `Orders`, and `s => s.Lines` gives `Lines`. The key that broke the rule is `Violation.Evidence`, and a ProblemDetails response is keyed by the member. A selector that ends in a call, such as `s => s.Orders.SelectMany(...)`, gives the collection before the call (`Orders`).

## Declared and resolved layers

Put a rule on the layer where the thing it forbids can exist. docs-builder expands `toc.yml` includes while loading, and guards against a loop while doing it, so the resolved tree **cannot** hold a
cycle; "no include cycle" is a rule about the include graph *before* expansion (a record of files and the paths they include). The resolved tree has its own rule (no file listed twice,
`Unique` over `Tree.Flatten`). Writing the cycle rule against the expanded tree would prove nothing, and generated data could never break it.

## Severity: errors reject, warnings and hints are noticed

`constraint.WithSeverity(Severity.Warning)` (or `Hint`) sets the severity of every violation of a rule, whatever shape it is. `Validate` returns all of them. `Apply` rejects a change only for
**errors**; warnings and hints ride along on `ApplyOutcome.Applied.Notices`, so a rule that is only a hint today can be written now without blocking anything. `Violation` carries `File`,
`Line`, `Column` and `Length`, and the item shapes take `at:` (a `Func<TItem, SourceLocation?>`) so a finding points at the line the offending item came from.

Mapping to your own findings type is one extension method on your side; for docs-builder's `Finding(Severity, Message, Line, Column, File, Length)`:

```csharp
public static Finding ToFinding(this Violation v) =>
    new(v.Severity switch { Nullean.Entities.Severity.Error => Severity.Error, Nullean.Entities.Severity.Warning => Severity.Warning, _ => Severity.Hint },
        v.Message, v.Line, v.Column, v.File, v.Length);
```

