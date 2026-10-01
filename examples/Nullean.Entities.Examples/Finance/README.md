# Finance

Four scenarios covering rules that appear in financial backends: accounts, transfers, ledgers, and loan origination.

---

## BankAccount

An account with a `Balance`, `OverdraftLimit`, and `Status` (Active, Frozen, Closed).

- Balance cannot drop below `−OverdraftLimit`.
- `Deposit` can only increase the balance — monotonicity is step-scoped so `Withdraw` is not constrained by it (`Monotonic`).
- A frozen account cannot change to any other status (`Transitions`).

Reactive extension: a withdrawal on a frozen account is never allowed; every withdrawal triggers a statement event within 24 h.

---

## Transfer

Moving an amount between two accounts, modelled as a constraint over the `(From, To)` pair.

- The sum of both balances must be unchanged by the move (`Conserves`).
- An account cannot transfer to itself.

`ApplyOutcome.Noop` fires on a zero-amount transfer — no constraints run, nothing is committed. Tests include a mutant (`LeakyMove` that creates money) to prove `Conserves` actually fires.

---

## Ledger

An accounting ledger with `Draft` and `Posted` journal entries, each with debit/credit lines.

- Every entry must balance: `Σ debits = Σ credits`.
- Every entry must have at least two lines.
- Posted entries are immutable — their lines cannot change once posted (`Transitions` with a `CheckTransition`).

Demonstrates `CheckTransition` for immutability rules and `GenAuto.Of<JournalEntry>` for generating structurally valid (but not necessarily balanced) entries — the mutant generator intentionally breaks balance.

---

## LoanApplication

An application moving through `Draft → Submitted → Approved / Rejected / Withdrawn`, with applicant data and supporting documents.

- Status transitions follow a defined allow-list (`Transitions`).
- Approval requires verified ID and Income documents, and a credit score ≥ 650 — two separate `When/Requires` rules so each violation is named independently.
- Requested amount must be positive.

Reactive extension: every submitted application must be resolved within 30 days (`After<Submitted>().Then<Approved, Rejected>().Within(30 days).Required()`).
