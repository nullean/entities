---
navigation_title: Why
description: What a type system cannot say, what it costs to say it by hand, and what changes when a rule is declared once.
---

# Why {{product}}

## A type says what shape a value has

A record with `Total`, `Status` and `Lines` says what an order holds. A union says which cases exist, and the compiler tells you every `switch` that must change when you add one. That is a lot, and it is free.

It stops at the rules that are about more than one value.

| You want to say | C# says it | Where it ends up |
|---|---|---|
| A total is never negative | no | a check in a constructor, and another in a validator |
| A confirmed order has lines | no | a service method |
| You can only ship a confirmed order | no | a `switch` in a handler |
| Cancelling twice is cancelling once | no | a test, if someone thought of it |
| A confirmed order ships within a minute | no | a runbook |

## The cost of saying it by hand

Each of these gets written again for each use: at the boundary, in the service, in the test. The copies drift. The tests cover the cases the author thought of, and a rule that is only about a sequence of events (a deadline, an order) usually has no test at all.

The bugs that cost the most live there. An older file overwrites a newer one. A retry changes state twice. Two rebuilds of the same input publish different output. A deadline is missed by one unlucky gap. [The gallery](bugs/index.md) has six, with the output each one produces.

## What changes

You declare the rule once, as a value with a name, next to the type. From then on:

- the same declaration checks the value where it is built,
- the same declaration is attacked by tests the library chooses from its shape,
- a failure names the rule, so you can filter it, escalate it, or turn it from a warning into an error on a date.

You still decide what the rules are. That is the part that needs thinking about the domain. The library takes over the part that does not: writing the test, choosing the inputs, and checking that the inputs reached the rule.
