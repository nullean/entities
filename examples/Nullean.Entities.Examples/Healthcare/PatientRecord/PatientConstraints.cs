// Three levels of constraint, each independently usable:
//
//   Level 1 — per-entity: MedicationConstraints, LabResultConstraints, VitalSignsConstraints
//             Validate a single child entity without needing the whole record.
//
//   Level 2 — collection: duplicates, completeness, cross-entity integrity inside the aggregate.
//             Uses Check to iterate collections, then delegates to level-1 sets to avoid
//             re-implementing the same rules at two levels.
//
//   Level 3 — cross-entity: the allergy/medication intersection check lives here because
//             it needs both collections — it cannot live on either child type alone.
//
// All three levels are standard [Constraints] companion classes; nothing in the API is
// special-cased for aggregates. The nesting is just method calls.

namespace Nullean.Entities.Examples.Healthcare.PatientRecord;

// ── Level 1: per-entity ────────────────────────────────────────────────────────

[Constraints]
public static partial class MedicationConstraints
{
	public static readonly Constraint<Medication> DrugNameNotEmpty = Constraints<Medication>.Always(
		m => !string.IsNullOrWhiteSpace(m.DrugName),
		"Drug name cannot be empty."
	);

	public static readonly Constraint<Medication> DosePositive = Constraints<Medication>.Always(m => m.Dose > 0, "Medication dose must be positive.");

	// When a medication has been discontinued, the discontinuation date must not precede the
	// prescription date — a common data-entry error in clinical systems.
	public static readonly Constraint<Medication> DiscontinuationAfterPrescription = Constraints<Medication>
		.When(m => m.DiscontinuedOn.HasValue)
		.Requires(m => m.DiscontinuedOn!.Value >= m.PrescribedOn, "Discontinuation date cannot be before the prescription date.");
}

[Constraints]
public static partial class LabResultConstraints
{
	public static readonly Constraint<LabResult> ReferenceRangeValid = Constraints<LabResult>.Always(
		l => l.ReferenceMin < l.ReferenceMax,
		"Reference range minimum must be less than maximum."
	);

	public static readonly Constraint<LabResult> TestNameNotEmpty = Constraints<LabResult>.Always(
		l => !string.IsNullOrWhiteSpace(l.TestName),
		"Lab test name cannot be empty."
	);
}

[Constraints]
public static partial class VitalSignsConstraints
{
	// Plausibility bounds — wide enough to cover edge cases (extreme athletes, neonates)
	// but narrow enough to catch data-entry transpositions.
	public static readonly Constraint<VitalSigns> WeightPlausible = Constraints<VitalSigns>.Always(
		v => v.WeightKg is > 0.3 and < 700,
		"Weight is outside a plausible human range."
	);

	public static readonly Constraint<VitalSigns> HeightPlausible = Constraints<VitalSigns>.Always(
		v => v.HeightCm is > 30 and < 280,
		"Height is outside a plausible human range."
	);

	public static readonly Constraint<VitalSigns> HeartRatePlausible = Constraints<VitalSigns>.Always(
		v => v.HeartRateBpm is >= 20 and <= 300,
		"Heart rate is outside a plausible range."
	);

	// Systolic must always exceed diastolic — a transposition here would be silently stored
	// without this guard, corrupting trend analysis.
	public static readonly Constraint<VitalSigns> SystolicAboveDiastolic = Constraints<VitalSigns>.Always(
		v => v.SystolicBp > v.DiastolicBp,
		"Systolic blood pressure must exceed diastolic blood pressure."
	);

	public static readonly Constraint<VitalSigns> RespiratoryRatePlausible = Constraints<VitalSigns>.Always(
		v => v.RespiratoryRate is >= 4 and <= 80,
		"Respiratory rate is outside a plausible range."
	);

	public static readonly Constraint<VitalSigns> TemperaturePlausible = Constraints<VitalSigns>.Always(
		v => v.TemperatureCelsius is >= 25.0 and <= 45.0,
		"Body temperature is outside a plausible range."
	);
}

// ── Level 2 & 3: aggregate root ───────────────────────────────────────────────

[Constraints]
public static partial class PatientRecordConstraints
{
	// Delegate to per-medication rules, tagging each violation with the drug name so the
	// caller knows which prescription caused the problem without digging into the evidence.
	public static readonly Constraint<PatientRecord> AllMedicationsValid = Constraints<PatientRecord>.Check(
		record =>
			[
				.. record
					.ActiveMedications
					.SelectMany(m => MedicationConstraints.Constraints.Validate(m).Select(v => v with { Message = $"Medication '{m.DrugName}': {v.Message}" })),
			]
	);

	// Duplicate drug detection: the same drug prescribed twice at the same time is almost
	// always a data-entry error, but can have serious clinical consequences.
	public static readonly Constraint<PatientRecord> NoDuplicateActiveMedications = Constraints<PatientRecord>.Check(
		record =>
			[
				.. record
					.ActiveMedications
					.Where(m => m.IsActive)
					.GroupBy(m => m.DrugName.Trim().ToLowerInvariant())
					.Where(g => g.Count() > 1)
					.Select(g => Violation.Error($"Drug '{g.Key}' appears more than once in active medications.") with { Evidence = g.ToList(), }),
			]
	);

	// Cross-entity: the most clinically dangerous constraint in this model. No active
	// medication may be a substance the patient is known to be allergic to.
	public static readonly Constraint<PatientRecord> NoAllergicMedications = Constraints<PatientRecord>.Check(record =>
	{
		var allergens = record.Allergies.Select(a => a.Substance.Trim().ToLowerInvariant()).ToHashSet();
		return [
			.. record
				.ActiveMedications
				.Where(m => m.IsActive && allergens.Contains(m.DrugName.Trim().ToLowerInvariant()))
				.Select(m =>
				{
					var allergy = record.Allergies.First(a => string.Equals(a.Substance, m.DrugName, StringComparison.OrdinalIgnoreCase));
					return Violation.Error($"Patient is {allergy.Severity} allergic to '{m.DrugName}'.") with { Evidence = (Medication: m, Allergy: allergy), };
				}),
		];
	});

	// Delegate to per-lab-result rules, tagging with the test name.
	public static readonly Constraint<PatientRecord> AllLabResultsValid = Constraints<PatientRecord>.Check(
		record =>
			[
				.. record
					.RecentLabResults
					.SelectMany(l => LabResultConstraints.Constraints.Validate(l).Select(v => v with { Message = $"Lab '{l.TestName}': {v.Message}" })),
			]
	);

	// Delegate to per-vital-signs rules, tagging with the recording timestamp.
	public static readonly Constraint<PatientRecord> AllVitalSignsPlausible = Constraints<PatientRecord>.Check(
		record =>
			[
				.. record
					.VitalHistory
					.SelectMany(
						v => VitalSignsConstraints.Constraints.Validate(v).Select(viol => viol with { Message = $"Vitals at {v.RecordedAt:u}: {viol.Message}" })
					),
			]
	);

	// Admitted patients must have vital signs recorded within the past 4 hours — a liveness
	// requirement that is meaningless for outpatients (step-gated with When).
	public static readonly Constraint<PatientRecord> AdmittedPatientsHaveRecentVitals = Constraints<PatientRecord>
		.When(r => r.Status == PatientStatus.Admitted)
		.Requires(
			r => r.VitalHistory.Any(v => (DateTimeOffset.UtcNow - v.RecordedAt).TotalHours <= 4),
			"Admitted patients must have vital signs recorded within the last 4 hours."
		);

	// Diagnosis ICD codes must not be blank — a code-only check; full ICD validation is
	// out of scope but the constraint structure would be identical.
	public static readonly Constraint<PatientRecord> AllDiagnosesHaveIcdCodes = Constraints<PatientRecord>.Check(
		record => [.. record.Diagnoses.Where(d => string.IsNullOrWhiteSpace(d.IcdCode)).Select(_ => Violation.Error("Every diagnosis must have an ICD code.")),]
	);

	// Step-scoped: only valid alongside a status-change mutation. Excluded from the general
	// set because Sequential constraints check (prev.Status, next.Status) — any other mutation
	// (e.g. AddMedication) that leaves Status unchanged would fail the allow-list check since
	// (Outpatient, Outpatient) is not an allowed arc. Same pattern as OrderLifecycle's
	// TotalNeverShrinksOnAddLine.
	[ConstraintIgnore]
	public static readonly Constraint<PatientRecord> LegalStatusTransitions = Constraints<PatientRecord>
		.Transitions(r => r.Status)
		.Allow(PatientStatus.Outpatient, PatientStatus.Admitted)
		.Allow(PatientStatus.Admitted, PatientStatus.Discharged);
}

// Step set for status-change mutations (Admit, Discharge).
// Use this set — not PatientRecordConstraints.Constraints — when changing patient status.
public static class PatientRecordStepConstraints
{
	public static readonly ConstraintSet<PatientRecord> StatusChangeStep = ConstraintSet.From(
		(nameof(PatientRecordConstraints.LegalStatusTransitions), PatientRecordConstraints.LegalStatusTransitions)
	);
}
