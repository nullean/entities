# Deployment Pipeline

**What is modelled:** A CI/CD deployment pipeline with multiple stages and an overall pipeline status.

**Rules, in plain English**
- Pipeline status transitions follow a defined allow-list (Queued → Running, Running → Succeeded/Failed/Cancelled).
- Commit SHA must not be empty.
- Completed pipelines (Succeeded/Failed/Cancelled) must record a completion timestamp.
- A succeeded pipeline must not have any failed stages.

**Reactive extension:** Page on-call when a production deployment fails.

**Tested axes:** invariant (Validate), channel Applied/Failure/Noop, transitions, CsCheck property.
