// Patient record aggregate root — the domain types only; no library references.
//
// PatientRecord is the aggregate root. It owns five child collections:
//   ActiveMedications, Allergies, RecentLabResults, VitalHistory, Diagnoses.
//
// The constraints in PatientConstraints.cs are layered in three levels:
//   1. Per-entity sets (MedicationConstraints, LabResultConstraints, VitalSignsConstraints)
//      — each can be validated standalone (unit-test a single Medication without a record).
//   2. Collection-level rules on the aggregate (duplicates, plausibility checks).
//   3. Cross-entity rules (prescribed medication ∩ known allergens = ∅).
//
// This layering is the key pattern for large aggregates: keep the per-entity rules close
// to the entity, delegate from the aggregate, enrich the violation message with context.

#pragma warning disable CA1724

namespace Nullean.Entities.Examples.Healthcare.PatientRecord;

// ── Child entity types ────────────────────────────────────────────────────────

public enum BloodType
{
	APositive,
	ANegative,
	BPositive,
	BNegative,
	ABPositive,
	ABNegative,
	OPositive,
	ONegative
}

public sealed record PatientDemographics(string Id, string FullName, DateOnly DateOfBirth, BloodType BloodType)
{
	public int AgeYears => (int)((DateTime.UtcNow - DateOfBirth.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25);
}

public enum DosageUnit
{
	Mg,
	Ml,
	Units
}

public enum Frequency
{
	Once,
	Daily,
	TwiceDaily,
	ThreeTimesDaily,
	AsNeeded,
	Weekly
}

public sealed record Medication(string DrugName, double Dose, DosageUnit Unit, Frequency Frequency, DateOnly PrescribedOn, DateOnly? DiscontinuedOn = null)
{
	// A medication is active until a discontinuation date is explicitly set.
	// Once discontinued (even today), it is no longer active.
	public bool IsActive => !DiscontinuedOn.HasValue;
}

public enum AllergySeverity
{
	Mild,
	Moderate,
	Severe,
	LifeThreatening
}

public sealed record Allergy(string Substance, AllergySeverity Severity);

public sealed record LabResult(string TestName, double Value, string Unit, double ReferenceMin, double ReferenceMax, DateTimeOffset RecordedAt)
{
	public bool IsWithinRange => Value >= ReferenceMin && Value <= ReferenceMax;
	public bool IsCritical => Value < ReferenceMin * 0.7 || Value > ReferenceMax * 1.5;
}

public sealed record VitalSigns(
	double WeightKg,
	double HeightCm,
	int SystolicBp,
	int DiastolicBp,
	int HeartRateBpm,
	int RespiratoryRate,
	double TemperatureCelsius,
	DateTimeOffset RecordedAt
)
{
	public double BmiKgPerM2 => WeightKg / Math.Pow(HeightCm / 100.0, 2);
}

public sealed record Diagnosis(string IcdCode, string Description, DateOnly OnsetDate, bool IsActive = true);

// ── The aggregate root ────────────────────────────────────────────────────────

public enum PatientStatus
{
	Outpatient,
	Admitted,
	Discharged
}

public sealed record PatientRecord(
	PatientDemographics Demographics,
	PatientStatus Status,
	IReadOnlyList<Medication> ActiveMedications,
	IReadOnlyList<Allergy> Allergies,
	IReadOnlyList<LabResult> RecentLabResults,
	IReadOnlyList<VitalSigns> VitalHistory,
	IReadOnlyList<Diagnosis> Diagnoses
)
{
	public VitalSigns? LatestVitals => VitalHistory.OrderByDescending(v => v.RecordedAt).FirstOrDefault();

	public PatientRecord AddMedication(Medication med) => this with { ActiveMedications = [.. ActiveMedications, med] };

	public PatientRecord Discontinue(string drugName) =>
		this with
		{
			ActiveMedications =
			[
				.. ActiveMedications.Select(
					m =>
						string.Equals(m.DrugName, drugName, StringComparison.OrdinalIgnoreCase)
							? m with { DiscontinuedOn = DateOnly.FromDateTime(DateTime.UtcNow) }
							: m
				),
			],
		};

	public PatientRecord RecordVitals(VitalSigns vitals) => this with { VitalHistory = [.. VitalHistory, vitals] };

	public PatientRecord RecordLabResult(LabResult result) => this with { RecentLabResults = [.. RecentLabResults, result] };

	public PatientRecord AddAllergy(Allergy allergy) => this with { Allergies = [.. Allergies, allergy] };

	public PatientRecord Admit() => this with { Status = PatientStatus.Admitted };
	public PatientRecord Discharge() => this with { Status = PatientStatus.Discharged };
}
