---
navigation_title: Related work
description: The projects in other ecosystems that {{product}} shares ideas with, what each does better, and what {{product}} adds.
---

# Related work

{{product}} is an executable specification: you declare a domain's rules once and validate, generate, test, monitor and document from them. None of the pieces are new. Property-based testing, model-based testing, model checking and runtime verification each have a long history, and if you have used any of the projects below, most of this library will feel familiar.

What {{product}} adds is the combination, in .NET, against the code you already have. It uses one rule type for validation at runtime, generated tests, stream monitoring and diagrams. It also checks an existing implementation against the model, so there is no second copy of the system to keep in step.

Each section below names what the project shares with {{product}}, what it does that {{product}} does not, and what {{product}} adds.

## Specs as data: Clojure spec and Malli

[clojure.spec](https://clojure.org/about/spec) and [Malli](https://github.com/metosin/malli) are the closest relatives in spirit. A spec is a value that validates data, explains a failure, and generates data for tests.

- **Shared:** one declaration that validates and generates. spec runs its generative tests on test.check, just as {{product}} runs on CsCheck.
- **They do better:** they describe the shape of data as well as its rules, and spec instruments functions to check their arguments during development. Shape in {{product}} comes from C# types.
- **{{product}} adds:** rules about steps and sequences, not just values: idempotency, monotonicity, conservation, temporal rules over event streams, and state machines. The shape of each rule picks its test, so you do not write the property.

## Model-based testing: QuickCheck, PropEr, Hypothesis, fast-check, proptest

Model-based testing generates sequences of commands, runs them against a model and the real system, and compares the two after each step. Erlang's [PropEr `statem`](https://proper-testing.github.io/tutorials/PropEr_testing_of_generic_servers.html), Hypothesis's [`RuleBasedStateMachine`](https://hypothesis.readthedocs.io/en/latest/stateful.html), fast-check's [model-based testing](https://fast-check.dev/docs/advanced/model-based-testing/) and Rust's [proptest state machines](https://proptest-rs.github.io/proptest/proptest/state-machine.html) all work this way. So does CsCheck, which {{product}} is built on.

- **Shared:** generated command sequences with preconditions, invariants checked after every step, and shrinking to the smallest failing sequence. `Script<TSut>`, `Spec(...).Sample()` and `Conform` are this pattern.
- **They do better:** fast-check's `scheduledModelRun` controls when async commands complete, to find race conditions. PropEr has parallel command testing.
- **{{product}} adds:** you do not write the model or the commands. An `EntitySpec` turns the entity's rules and its generated `Actions` into the walks, and `Conform` drives your implementation down the same walks. The vacancy check fails a test whose inputs never reached a rule.

## Model-driven state machines: XState

[`@xstate/test`](https://stately.ai/docs/xstate-test) (moving into `@xstate/graph`) generates test plans from a state machine, "based on the shortest paths from the test model's initial state to every other reachable state", and you supply the code that drives the real app.

- **Shared:** the machine is the specification, and tests are walks over it. `ConformToExhaustive` checks every transition of a machine against your entity, each down a shortest path.
- **XState does better:** it is also a production runtime with a visual editor, and it is aimed at UI flows.
- **{{product}} adds:** guards over case values, structural checks when the machine is defined, Mermaid in and out, and machines whose states are union case types. Compared with Stateless in .NET, which runs a machine but does not test against it, {{product}} does both.

## Model checkers: TLA+, Quint, P, Alloy and Stateright

Model checkers explore every reachable state of a model. They prove a property holds or return the shortest trace that breaks it.

- [TLA+](https://lamport.azurewebsites.net/tla/tla.html) and [Quint](https://quint.sh/) ("executable specifications for reliable systems") are specification languages. You write the model in a separate language, check it with TLC or by simulation, and replay traces against the real code (for Rust, Quint's `quint-connect` does this).
- [P](https://p-org.github.io/P/whatisP/) models a system as communicating state machines, explores interleavings of messages and failures, and with PObserve "validates service logs and execution traces from the running system" against the same specifications.
- [Alloy](https://alloytools.org/) checks relational models: keys, references, acyclic structures.
- [Stateright](https://github.com/stateright/stateright) is "a model checker for implementing distributed systems" in Rust. You write the system as Stateright actors, check `always`, `sometimes` and `eventually` properties, then run the same actors on a real network.

**Shared:** invariants over every reachable state (`Deeper()`'s exhaustive check), shortest counterexamples, liveness (`Eventually<T>`), and relational rules (`Unique`, `References`, `Acyclic`).

**They do better:** this is where {{product}} has the largest gap.

- Concurrency. They explore every interleaving of messages between many nodes, over networks that drop, duplicate or reorder. {{product}} explores sequences of operations on one entity.
- Scale. Symmetry reduction and dedicated checkers handle state spaces {{product}}'s exhaustive walk cannot.
- Tooling. Stateright has an explorer UI for stepping through the state space, plus linearizability and sequential-consistency testers.

**{{product}} adds:** no separate model and no rewrite. TLA+, Quint and P model the system in another language, and Stateright asks you to write it as Stateright actors. {{product}} checks the types and services you already ship. `Conform`, `ConformToMutable` and `ConformToServiceAsync` drive a real implementation against the model. The same rules also validate at runtime, which no model checker does.

## Runtime verification and assertion SDKs: Antithesis

Runtime verification checks temporal properties against a trace as it happens. The [Antithesis SDK](https://antithesis.com/docs/properties_assertions/assertions/) has the closest vocabulary: `always`, `sometimes`, `reachable` and `unreachable` assertions, where a `sometimes` assertion "is listed as failing by default until proven by a passing encounter".

- **Shared:** `Always`, `Never`, `Eventually` and `After().Then().Within()` over an event stream. The vacancy check plays the role of `sometimes`: a rule that was never reached is a failure, not a pass.
- **Antithesis does better:** it runs the whole system in a deterministic simulator and searches for the inputs and timings that break an assertion.
- **{{product}} adds:** the assertions run in-process, in a unit test or against a live `IObservable` stream, with no simulator.

## Contracts and provers: Eiffel, Code Contracts, Kani

Design by contract, from Eiffel and .NET's Code Contracts, writes preconditions, postconditions and invariants into the methods themselves. Provers such as [Kani](https://github.com/model-checking/kani), "a bit-precise model checker for Rust", check a harness over every possible input.

- **Shared:** invariants that belong to a type.
- **They do better:** a prover proves. {{product}} samples, except where `Deeper()` closes a small state space.
- **{{product}} adds:** a rule is a named value outside the method, so it can be listed, reused for validation, ProblemDetails and options, and attacked by generated tests.
