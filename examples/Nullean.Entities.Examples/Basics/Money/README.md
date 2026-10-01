# Money

**What is modelled:** A monetary amount with a currency code.

**Rules, in plain English**
- An amount cannot be negative.
- A currency code cannot be empty.
- Splitting a monetary value into two parts must not create or destroy any amount (`Conserves` over the pair).

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, CsCheck conservation property, mutant that proves Conserves fires.
