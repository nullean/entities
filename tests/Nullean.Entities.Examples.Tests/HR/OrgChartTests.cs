using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.HR.OrgChart;

namespace Nullean.Entities.Examples.Tests.HR;

public class OrgChartTests
{
	private static readonly ConstraintSet<Person> PersonSet = PersonConstraints.All;
	private static readonly ConstraintSet<OrgChart> OrgSet = OrgChartConstraints.All;

	private static Person ActivePerson(string id, string? managerId = null) => new(id, $"Person {id}", managerId, EmploymentStatus.Active, 1, []);

	[Test]
	public void Validate_valid_person_passes()
	{
		PersonSet.Validate(ActivePerson("P1")).Should().BeEmpty();
	}

	[Test]
	public void Validate_terminated_person_with_reports_fails()
	{
		var bad = ActivePerson("P1") with { Status = EmploymentStatus.Terminated, DirectReportIds = ["P2"] };
		PersonSet.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_org_with_valid_manager_reference_passes()
	{
		var org = new OrgChart([ActivePerson("P1"), ActivePerson("P2", "P1")]);
		OrgSet.Validate(org).Should().BeEmpty();
	}

	[Test]
	public void Validate_org_with_missing_manager_fails()
	{
		var org = new OrgChart([ActivePerson("P2", "GHOST")]);
		OrgSet.Validate(org).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_org_with_cycle_fails()
	{
		// P1 → manager P2 → manager P1
		var p1 = ActivePerson("P1", "P2");
		var p2 = ActivePerson("P2", "P1");
		var org = new OrgChart([p1, p2]);
		OrgSet.Validate(org).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_terminate_removes_reports_and_manager()
	{
		var person = ActivePerson("P1", "boss").AddReport("P2");
		var channel = ConstrainedValue.For(person).Enforce(PersonSet);
		var result = channel.ApplyDetailed(p => p.Terminate());
		result.Should().BeOfType<ApplyOutcome<Person>.Applied>();
		((ApplyOutcome<Person>.Applied)result).Value.DirectReportIds.Should().BeEmpty();
	}

	[Test]
	public void CsCheck_linear_chain_has_no_cycles()
	{
		Gen.Int[2, 10].Sample(n =>
		{
			// P0, P1 → P0, P2 → P1, ... no cycles
			var people = Enumerable.Range(0, n).Select(i => ActivePerson($"P{i}", i > 0 ? $"P{i - 1}" : null)).ToList();
			var org = new OrgChart(people);
			OrgSet.Validate(org).Should().BeEmpty();
		});
	}
}
