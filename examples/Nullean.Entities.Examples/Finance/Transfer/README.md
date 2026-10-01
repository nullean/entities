# Money Transfer

**What is modelled:** Moving a decimal amount between two accounts.

**Rules, in plain English**
- The total balance across both accounts must be unchanged by a transfer (`Conserves`).
- An account cannot transfer to itself.

**Tested axes:** invariant (Validate), channel Applied/Noop (zero amount), conservation property, mutant that proves Conserves fires.
