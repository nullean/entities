---
navigation_title: Why
description: Types say what a thing is. Nothing says what it must do. {{product}} makes those rules first-class, and then tests them on axes you have probably never considered.
---

# Why {{product}}

**Types say what a thing is. Nothing says what it must do.**

A record with `Total`, `Status` and `Lines` tells the compiler what an order holds. It does not tell anything that a total is never negative, that you can only ship a confirmed order, that cancelling twice is cancelling once, or that a confirmed order ships within a minute. The compiler is excellent at shape and silent on behaviour.

| You want to say | The type system says it? |
|---|---|
| An order total is never negative | no |
| A confirmed order has at least one line | no |
| You can only ship a confirmed order | no |
| Cancelling twice is cancelling once | no |
| A version never goes back | no |
| A confirmed order ships within a minute | no |

## So the rules go everywhere, and drift

Without a home, the same rule is written again for each use:

```csharp
// in the constructor
if (lines.Count == 0 && status == Confirmed) throw new ArgumentException("...");
// in the validator
RuleFor(o => o).Must(o => o.Status != Confirmed || o.Lines.Count > 0);
// in a test
Assert.True(order.Status != Confirmed || order.Lines.Count > 0);
```

Three copies, three wordings, no name. One gets updated and the others do not. And the rules that are about more than one value (a retry, an order of events, a deadline) usually have no copy at all, because nobody wrote that test.

## Make the rule a first-class thing

{{product}} gives a rule a home and a name: declared once, next to the type, as a value.

```csharp
public static readonly Constraint<Order> ConfirmedOrdersHaveLines = Constraints<Order>
	.When(o => o.Status == OrderStatus.Confirmed)
	.Requires(o => o.Lines.Count > 0, "A confirmed order has at least one line.");
```

This is not only validation. The same declaration:

1. **Validates** a value where it is built, with a message that names the rule.
2. **Is tested**, automatically, on generated data. You write no test.
3. **Watches** a live stream of events, with the same vocabulary.

Because a rule has a name, a finding can be filtered, counted, escalated, and (for a rule you are introducing) reported as a warning that becomes an error on a date.

## The tests ask questions you did not think to ask

Declaring a rule is the easy part. The part nobody else gives you is what happens next. The *shape* of a rule tells the library what to test, so it tests things a scenario test almost never covers:

- Does doing it twice change anything?
- Does a number that must only grow ever go back?
- Did anything appear or vanish between two halves of a change?
- Can this bad sequence of events ever happen?
- Does the second step follow the first within the deadline, whatever the timing?
- Does the answer depend on the order the inputs arrived in?
- And the question that keeps all the others honest: **did the test actually reach the rule?**

That is [nine questions](axes/index.md). Here is the test:

```csharp
AutoTest.For(OrderConstraints.Instance, orders).Run();
```

Have you ever tested whether a retry changes state, whether arrival order changes the output, or whether a version can go back? Most teams have tested two or three of the nine. The other six are where the bugs that reach production live, because nobody thought to look.

## What it is not

It is not a replacement for your types or for example tests. It does not do I/O. It does not guess your rules: you decide what must be true, which is the part that needs thinking about the domain. It takes over the rest: writing the test, choosing the inputs, and checking that they reached the rule. See [known limitations](known-limitations.md).

## Next

- [Concepts](concepts.md): the ten words, in plain words.
- [Getting started](getting-started/index.md): one order, one red test, five minutes.
- [The nine axes](axes/index.md): the questions, one page each.
