---
navigation_title: "09 Vacancy"
description: "Axis 09, Vacancy: Did the test even reach the rule?"
---

# Axis 09 · Vacancy: Did the test even reach the rule?

## In plain words

A smoke alarm test where nobody lit a match. The alarm did not go off, and the test passed, and you learned nothing. A test is *vacuous* when the data never reached the rule it was supposed to check.

## The bug you would ship

Every rule held and the suite was green. But the generator only made pending orders, so the rule about confirmed orders was never checked. A green run, and no evidence at all.

## You declare

You declare nothing. This axis is automatic, and it runs on every other axis.

## The library does

It checks that every rule *can* fire (it finds or is given a value that breaks the rule, and only that rule). It checks that a rule that applies "when X" saw an X. And each other axis checks its own version of the same thing: the operation changed something, the value moved, an order had two items, a run passed the deadline.

## When it fails, you read

:::{include} ../_snippets/vacancy.txt
:language: text
:::

How to read this:

- **Rule** names the rule that was never exercised, and **Not exercised because** says why.
- **Next** says what to change, usually the generator, so the data reaches the rule.

## Why you did not write this test

This is the one that makes the other eight trustworthy. A property test that passes has not told you the property holds; it has told you it did not find a counter-example. If it never looked, that means nothing.

## In real code

In docs-builder, this axis found that the changelog entry generator never produced a marker entry, so none of the marker rules had ever run.

---

Previous: [08 Confluence](08-confluence.md) · [All nine axes](index.md)
