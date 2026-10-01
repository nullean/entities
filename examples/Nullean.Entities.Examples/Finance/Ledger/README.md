# Ledger

**What is modelled:** An accounting ledger with draft and posted journal entries, each with debit/credit lines.

**Rules, in plain English**
- Every journal entry must balance: total debits equal total credits.
- A journal entry must have at least two lines.
- Posted entries are immutable — their lines cannot change once posted (`Transitions`).

**Tested axes:** invariant (Validate), channel Applied/Failure, CsCheck with GenAuto.Of.
