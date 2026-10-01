using AwesomeAssertions;
using Nullean.Entities.Examples.Scheduling.ShiftRoster;

namespace Nullean.Entities.Examples.Tests.Scheduling;

public class ShiftRosterTests
{
	private static readonly ConstraintSet<Employee> EmpConstraints = EmployeeConstraints.All;
	private static readonly ConstraintSet<Roster> RosterConstraints_ = RosterConstraints.All;

	private static Shift Shift(int dayOfMonth, int startHour, int endHour) =>
		new(new DateTimeOffset(2024, 6, dayOfMonth, startHour, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 6, dayOfMonth, endHour, 0, 0, TimeSpan.Zero));

	[Test]
	public void Validate_employee_within_forty_hours_passes()
	{
		var emp = new Employee(
			"E1",
			"Alice",
			[
				Shift(3, 9, 17), // 8h

				Shift(4, 9, 17) // 8h — same week, total 16h

			]
		);
		EmpConstraints.Validate(emp).Should().BeEmpty();
	}

	[Test]
	public void Validate_employee_over_forty_hours_fails()
	{
		var shifts = Enumerable
			.Range(3, 6)
			.Select(d => Shift(d, 8, 17)) // 9h * 6 = 54h

			.ToList();
		var emp = new Employee("E1", "Alice", shifts);
		EmpConstraints.Validate(emp).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_employee_insufficient_rest_fails()
	{
		var emp = new Employee(
			"E1",
			"Bob",
			[
				Shift(3, 9, 17), // ends 17:00

				Shift(3, 22, 23) // starts 22:00 — only 5h rest

			]
		);
		EmpConstraints.Validate(emp).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_valid_roster_passes()
	{
		var emp = new Employee("E1", "Carol", [Shift(3, 9, 17)]);
		var roster = new Roster([emp]);
		RosterConstraints_.Validate(roster).Should().BeEmpty();
	}
}
