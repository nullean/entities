# Healthcare — Patient Record

A `PatientRecord` is an aggregate root that owns five child collections: active medications, allergies, recent lab results, vital-sign history, and diagnoses. It is the most structurally complex example in the suite.

## What is modelled

A clinical record system where a patient's state must satisfy rules at three levels simultaneously:

1. **Per-entity rules** — each `Medication`, `LabResult`, and `VitalSigns` record is independently valid before it can join the aggregate.
2. **Collection-level rules** — no duplicate active prescriptions; all per-entity rules re-checked in aggregate context with enriched error messages.
3. **Cross-entity rules** — the most clinically dangerous: no active medication may match a known allergen.

## Rules, in plain English

**Medication (per-entity)**
- Drug name is not empty.
- Dose is positive.
- When a discontinuation date is recorded, it cannot precede the prescription date.

**Lab result (per-entity)**
- Test name is not empty.
- Reference range minimum is less than maximum.

**Vital signs (per-entity)**
- Weight, height, heart rate, respiratory rate and body temperature are within plausible human ranges.
- Systolic blood pressure exceeds diastolic (a transposition is silently dangerous without this guard).

**Patient record (aggregate)**
- All active medications satisfy the per-medication rules — violation messages are enriched with the drug name.
- No drug is prescribed more than once in the active medication list.
- No active medication is a substance the patient is allergic to (`NoAllergicMedications` — the cross-entity constraint).
- All lab results satisfy the per-result rules — violation messages are enriched with the test name.
- All vital signs satisfy the per-vital rules — violation messages are enriched with the recording timestamp.
- An admitted patient must have vital signs recorded within the last 4 hours (`When/Requires`).
- Every diagnosis has an ICD code.
- Patient status follows a defined transition allow-list: `Outpatient → Admitted → Discharged`.

## How it is validated

`PatientRecordConstraints.Constraints.Validate(record)` runs everything and returns a flat list of named violations. Because each level tags its violations with context (drug name, test name, timestamp), a caller can display or group them without re-querying the aggregate.

Each level is also independently testable:

```csharp
// Test a single Medication without constructing a full PatientRecord:
MedicationConstraints.Constraints.Validate(new Medication("", 0, ...)).Should().HaveCount(2);

// Test the cross-entity allergy check in isolation:
PatientRecordConstraints.Constraints.Validate(recordWithAllergyConflict)
    .Should().ContainSingle(v => v.ConstraintId == "NoAllergicMedications");
```

The `ChannelResult<PatientRecord>` pattern is demonstrated for the `AddMedication` and `Admit` mutations — invalid state is never committed.

## Why nested constraints matter here

Without delegation, the aggregate would re-implement "dose must be positive" and "reference range is valid" inline. Any change to the child-level rule would need to be made in two places. With delegation, `MedicationConstraints.Constraints.Validate(m)` is called once per medication, and the aggregate only adds the context it uniquely has (the drug name in the violation message). The child rules remain unit-testable in isolation.
