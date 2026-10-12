---
navigation_title: Counter-examples
description: Why every rule needs a counter-example, how the library finds most of them, and what is left for you to write.
---

# Counter-examples

A *counter-example* (see [concepts](../concepts.md)) is a valid value changed so that exactly one rule breaks. It answers one question: can this rule ever fire? Without one, a test that passes shows nothing, because the rule might be unreachable. That is the [fourth principle](../design-principles/index.md).

## You write only what a machine cannot find

When a rule has no counter-example, the library searches for one, deterministically, from valid values. It tries single changes:

- a member set to an edge value (null, blank, empty, a very long string, `NaN`, an undefined enum),
- a member taken from another valid value,
- a bool flipped, an enum set to another value,
- a value a day or one unit either side of the current one,
- an item in a collection copied, removed, reversed, or added as a changed copy,
- two members of the same type swapped,
- any of these on a member of a member, an item of a collection, or a value of a dictionary, up to three levels down,
- for a rule that only applies when a guard is true, the guard is made true first, then the requirement is broken.

It keeps the first change that breaks one rule and no other. If every change breaks several, it accepts the one that breaks the fewest. On the docs-builder domain, with every hand-written counter-example removed, it found one for 82 percent of the rules.

What is left is what only you know: the value `"unknown"` that a field must not hold, a path that leaves the folder it must stay in, a structure built to your domain's own shape.

## See what is found, and what is left

```csharp
var report = AutoTest.For(MyConstraints.Instance, gen).CounterExampleReport();
```

For each rule, the report says whether you gave a counter-example, the search found one (with a few words on what it changed, for example `Owner.Label = ""`), or nothing was found. Run it once on a new type to see what you still have to write. `report.Missing` lists the rules.

With `CounterExampleReport(alsoSearchGiven: true)`, a rule you wrote one for also says whether the search would have found it. Those are the ones you can delete.

## Give one

```csharp
AutoTest.For(MyConstraints.Instance, gen)
	.CounterExample(MyConstraints.PathStaysInsideTheFolder, d => d with { Path = "../outside" })
```

The overload that takes the rule object means a rename cannot orphan the counter-example and a typo does not compile. A string-keyed dictionary also works: `.CounterExamples(new Dictionary<string, Func<T, T>> { ["rule-name"] = ... })`.

A counter-example you give is held to a stricter standard than one the library finds: it must break its rule, and only its rule, so a failure of that rule points at one cause.

## Configure the values

If your valid values need the options of your generator (a file system, a normalizer for a derived member), pass them once and the search uses them too:

```csharp
AutoTest.For(MyConstraints.Instance, gen).Options(o => o.UseFileSystem())
```

## Arguments for operations

For an operation with an argument, the library draws the argument from the start value: half the draws take a value of that type from inside the start (an entry the registry already holds), and a third of those change one member a little. That reaches an operation that decides on existing data (replace only if newer) with no generator written. If you want a different argument, pass a generator that sees the start: `.Action("Name", apply, arguments: start => ...)`.
