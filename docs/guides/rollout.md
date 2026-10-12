---
navigation_title: Rolling out a rule
description: Add a rule as a warning, pin its severity, and turn it into an error on a date, without breaking a build that is green today.
---

# Rolling out a rule

A rule that is true of your data today but has never been enforced cannot become an error the day you write it. Declare it with a severity and a date.

## Start as a warning

```csharp
public static readonly Constraint<Docset> NoEmptyTocEntries = Constraints<Docset>
	.Always(d => d.Toc.All(t => t.Children.Count > 0), "A table of contents entry is not empty.")
	.WithSeverity(Severity.Warning);
```

Findings are reported with the rule's name, so you can see how many there are and where. Nothing breaks.

## Turn it into an error on a date

```csharp
.WithSeverityUntil(Severity.Warning, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), Severity.Error)
```

Before the date it is a warning. From the date on it is an error. A test controls the date through the `TimeProvider` it passes, so you can check both sides.

## Pin the level

A rule that was an error must stay an error, and a rule weakened by accident should be a visible diff. Keep a ledger: a test that lists every rule with its declared severity and compares it with a checked-in file. docs-builder does this for its 287 rules; a change to a severity shows up in review as a one-line diff.

## Operations and warnings

An operation is allowed to produce a value that only breaks a warning, because the type lets it through and reports it. The Invariant axis checks that operations never produce a value that breaks an error. A rule you want enforced on operations is a rule you make an error.
