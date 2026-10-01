# Email Address

**What is modelled:** A validated e-mail address string.

**Rules, in plain English**
- The value cannot be empty or whitespace.
- The value must contain both `@` and `.` (basic format check; `Check` lets the violation embed the offending string).

**Tested axes:** invariant (Validate), channel Applied/Failure, CsCheck property.
