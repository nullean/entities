# Bank Account

**What is modelled:** A bank account with a balance, overdraft limit, and status.

**Rules, in plain English**
- The balance cannot drop below the negative overdraft limit.
- Depositing can only increase the balance (`Monotonic`, step-scoped to Deposit).
- A frozen account cannot change to any other status.

**Reactive extension:** A withdrawal on a frozen account is never allowed; withdrawals trigger a statement event within 24 hours.

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, CsCheck property, transitions.
