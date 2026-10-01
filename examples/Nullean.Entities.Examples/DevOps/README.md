# DevOps — Deployment pipeline

A CI/CD deployment pipeline with named stages and an overall pipeline status.

**Rules:**

- Pipeline status transitions follow a defined allow-list: `Queued → Running → Succeeded / Failed / Cancelled`.
- Commit SHA must not be empty.
- Completed pipelines (Succeeded, Failed, Cancelled) must record a completion timestamp.
- A succeeded pipeline must not have any failed stages.

**Reactive extension:**

`Never<StageFailed, StageStarted>` — no new stage may start after a failure. Every started stage must resolve (succeed or fail) within 30 minutes (`After<StageStarted>().Then<StageSucceeded, StageFailed>().Within(30min).Required()`).
