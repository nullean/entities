using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Healthcare.PatientRecord;

namespace Nullean.Entities.Examples.Tests.Healthcare;

public class PatientRecordTests
{
	// ── Fixtures ──────────────────────────────────────────────────────────────

	private static PatientDemographics Demographics() => new("P001", "Alice Smith", new DateOnly(1980, 6, 15), BloodType.APositive);

	private static Medication Aspirin() => new("Aspirin", 100, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow));

	private static Medication Ibuprofen() => new("Ibuprofen", 400, DosageUnit.Mg, Frequency.ThreeTimesDaily, DateOnly.FromDateTime(DateTime.UtcNow));

	private static Allergy AspirinAllergy() => new("Aspirin", AllergySeverity.Severe);

	private static LabResult NormalGlucose() => new("Glucose", 5.5, "mmol/L", 3.9, 6.1, DateTimeOffset.UtcNow);

	private static VitalSigns NormalVitals() => new(70, 170, 120, 80, 72, 16, 36.8, DateTimeOffset.UtcNow);

	private static PatientRecord HealthyOutpatient() => new(Demographics(), PatientStatus.Outpatient, [Aspirin()], [], [NormalGlucose()], [NormalVitals()], []);

	// ── Level 1: per-entity constraints ───────────────────────────────────────

	[Test]
	public void Medication_valid_has_no_violations()
	{
		MedicationConstraints.Constraints.Validate(Aspirin()).Should().BeEmpty();
	}

	[Test]
	public void Medication_empty_drug_name_is_invalid()
	{
		var med = new Medication("", 100, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow));
		var violations = MedicationConstraints.Constraints.Validate(med);
		violations.Should().ContainSingle(v => v.ConstraintId == "DrugNameNotEmpty");
	}

	[Test]
	public void Medication_zero_dose_is_invalid()
	{
		var med = new Medication("Aspirin", 0, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow));
		MedicationConstraints.Constraints.Validate(med).Should().ContainSingle(v => v.ConstraintId == "DosePositive");
	}

	[Test]
	public void Medication_discontinuation_before_prescription_is_invalid()
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var med = new Medication("Aspirin", 100, DosageUnit.Mg, Frequency.Daily, today, today.AddDays(-1));
		MedicationConstraints.Constraints.Validate(med).Should().ContainSingle(v => v.ConstraintId == "DiscontinuationAfterPrescription");
	}

	[Test]
	public void LabResult_inverted_reference_range_is_invalid()
	{
		var lab = new LabResult("Glucose", 5.5, "mmol/L", 10.0, 3.9, DateTimeOffset.UtcNow);
		LabResultConstraints.Constraints.Validate(lab).Should().ContainSingle(v => v.ConstraintId == "ReferenceRangeValid");
	}

	[Test]
	public void VitalSigns_valid_has_no_violations()
	{
		VitalSignsConstraints.Constraints.Validate(NormalVitals()).Should().BeEmpty();
	}

	[Test]
	public void VitalSigns_systolic_below_diastolic_is_invalid()
	{
		var transposed = NormalVitals() with { SystolicBp = 70, DiastolicBp = 120 };
		VitalSignsConstraints.Constraints.Validate(transposed).Should().ContainSingle(v => v.ConstraintId == "SystolicAboveDiastolic");
	}

	[Test]
	public void VitalSigns_implausible_heart_rate_is_invalid()
	{
		var bad = NormalVitals() with { HeartRateBpm = 5 };
		VitalSignsConstraints.Constraints.Validate(bad).Should().ContainSingle(v => v.ConstraintId == "HeartRatePlausible");
	}

	// ── Level 2: aggregate collection rules ───────────────────────────────────

	[Test]
	public void Aggregate_valid_outpatient_has_no_violations()
	{
		PatientRecordConstraints.Constraints.Validate(HealthyOutpatient()).Should().BeEmpty();
	}

	[Test]
	public void Aggregate_invalid_medication_surfaces_with_drug_name_prefix()
	{
		var badMed = new Medication("", 0, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow));
		var record = HealthyOutpatient() with { ActiveMedications = [badMed] };

		var violations = PatientRecordConstraints.Constraints.Validate(record);

		violations.Should().NotBeEmpty();
		violations.Should().AllSatisfy(v => v.Message.Should().StartWith("Medication '':"));
	}

	[Test]
	public void Aggregate_duplicate_active_medication_is_invalid()
	{
		var record = HealthyOutpatient() with { ActiveMedications = [Aspirin(), Aspirin()] };

		PatientRecordConstraints.Constraints.Validate(record).Should().ContainSingle(v => v.ConstraintId == "NoDuplicateActiveMedications");
	}

	// ── Level 3: cross-entity constraint ─────────────────────────────────────

	[Test]
	public void Aggregate_medication_matching_allergen_is_invalid()
	{
		var record = HealthyOutpatient() with { Allergies = [AspirinAllergy()] };

		var violations = PatientRecordConstraints.Constraints.Validate(record);

		violations.Should().ContainSingle(v => v.ConstraintId == "NoAllergicMedications");
		violations.Single(v => v.ConstraintId == "NoAllergicMedications").Message.Should().Contain("Aspirin");
	}

	[Test]
	public void Aggregate_discontinuing_allergenic_drug_clears_the_violation()
	{
		var record = HealthyOutpatient() with { Allergies = [AspirinAllergy()] };

		var stopped = record.Discontinue("Aspirin");

		PatientRecordConstraints.Constraints.Validate(stopped).Should().NotContain(v => v.ConstraintId == "NoAllergicMedications");
	}

	[Test]
	public void Aggregate_non_overlapping_allergen_does_not_trigger_cross_entity_rule()
	{
		var penicillinAllergy = new Allergy("Penicillin", AllergySeverity.LifeThreatening);
		var record = HealthyOutpatient() with { Allergies = [penicillinAllergy] };

		PatientRecordConstraints.Constraints.Validate(record).Should().NotContain(v => v.ConstraintId == "NoAllergicMedications");
	}

	// ── Channel: mutations are guarded ────────────────────────────────────────

	[Test]
	public void Channel_Applied_on_adding_a_non_allergenic_medication()
	{
		var channel = ConstraintChannel.For(HealthyOutpatient()).Enforce(PatientRecordConstraints.Constraints);

		channel.Apply(r => r.AddMedication(Ibuprofen())).Should().BeOfType<ChannelResult<PatientRecord>.Applied>();
	}

	[Test]
	public void Channel_Failure_when_adding_a_medication_matching_an_allergen()
	{
		var record = HealthyOutpatient() with { Allergies = [AspirinAllergy()] };
		var channel = ConstraintChannel.For(record).Enforce(PatientRecordConstraints.Constraints);

		// Aspirin is already in the record, but allergy was added via 'with' — adding Aspirin again
		// would also fail; here we test that the existing record with the conflict fails on Apply.
		var result = channel.Apply(
			r => r.AddMedication(new Medication("Aspirin", 500, DosageUnit.Mg, Frequency.TwiceDaily, DateOnly.FromDateTime(DateTime.UtcNow)))
		);

		result.Should().BeOfType<ChannelResult<PatientRecord>.Failure>();
		channel.Current.Should().Be(record); // unchanged
	}

	[Test]
	public void Channel_Failure_on_invalid_status_transition()
	{
		var discharged = HealthyOutpatient() with { Status = PatientStatus.Discharged };
		var channel = ConstraintChannel.For(discharged).Enforce(PatientRecordStepConstraints.StatusChangeStep);

		// Discharged → Admitted is not allowed
		channel.Apply(r => r.Admit()).Should().BeOfType<ChannelResult<PatientRecord>.Failure>();
	}

	[Test]
	public void Channel_Applied_on_Outpatient_to_Admitted()
	{
		var channel = ConstraintChannel.For(HealthyOutpatient()).Enforce(PatientRecordStepConstraints.StatusChangeStep);

		channel.Apply(r => r.Admit()).Should().BeOfType<ChannelResult<PatientRecord>.Applied>();
	}

	// ── CsCheck properties ────────────────────────────────────────────────────

	[Test]
	public void Property_valid_medications_always_pass_per_entity_constraints()
	{
		// Gen.Int guarantees a unique non-empty non-whitespace drug name.
		// Gen.Int cast to double avoids Gen.Double's internal Where filter.
		var genMed = Gen.Select(
			Gen.Int[1, 10000].Select(i => $"Drug{i}"),
			Gen.Int[1, 1000].Select(d => (double)d),
			(name, dose) => new Medication(name, dose, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow))
		);

		genMed.Sample(m => MedicationConstraints.Constraints.Validate(m).Count == 0);
	}

	[Test]
	public void Property_valid_vitals_always_pass_per_entity_constraints()
	{
		// All ranges chosen to satisfy the plausibility bounds in VitalSignsConstraints.
		var genVitals = Gen.Select(Gen.Int[1, 499].Select(v => (double)v), Gen.Int[31, 279].Select(
			v => (double)v
		), Gen.Int[21, 299], Gen.Int[5, 79], Gen.Int[0, 19].Select(v => 25.0 + v), (weight, height, hr, rr, temp) =>
		{
			var systolic = Gen.Int[81, 220].Single();
			var diastolic = Gen.Int[40, systolic - 1].Single();
			return new VitalSigns(weight, height, systolic, diastolic, hr, rr, temp, DateTimeOffset.UtcNow);
		});

		genVitals.Sample(v => VitalSignsConstraints.Constraints.Validate(v).Count == 0);
	}

	[Test]
	public void Mutant_zero_dose_always_fails_per_entity_check()
	{
		var act =
			() => Gen
				.Int[1, 10000]
				.Select(i => new Medication($"Drug{i}", 0, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow)))
				.Sample(m => MedicationConstraints.Constraints.Validate(m).Count == 0);
		act.Should().Throw<Exception>();
	}

	[Test]
	public void Mutant_cross_entity_allergy_conflict_always_fails_aggregate_check()
	{
		// Build records where the medication and allergen match — must always produce a violation.
		var act =
			() => Gen
				.Int[1, 10000]
				.Select(i => $"Drug{i}")
				.Select(drug =>
				{
					var med = new Medication(drug, 100, DosageUnit.Mg, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow));
					var allergy = new Allergy(drug, AllergySeverity.Severe);
					return HealthyOutpatient() with { ActiveMedications = [med], Allergies = [allergy], };
				})
				.Sample(r => PatientRecordConstraints.Constraints.Validate(r).Count == 0);

		act.Should().Throw<Exception>();
	}
}
