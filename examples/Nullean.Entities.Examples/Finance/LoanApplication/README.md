# Loan Application

**What is modelled:** A loan application moving through Draft → Submitted → Approved/Rejected/Withdrawn, with applicant data and supporting documents.

**Rules, in plain English**
- State transitions follow a defined allow-list (`Transitions`).
- An Approved application must have verified ID and Income documents (`When/Requires`).
- An Approved application must have a credit score of at least 650 (`When/Requires`).
- The requested amount must be positive.

**Reactive extension:** Every submitted application must be resolved (approved, rejected, or withdrawn) within 30 days.

**Tested axes:** invariant (Validate), channel Applied/Failure, transitions, CsCheck property.
