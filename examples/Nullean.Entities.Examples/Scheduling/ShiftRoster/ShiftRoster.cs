namespace Nullean.Entities.Examples.Scheduling.ShiftRoster;

public sealed record Shift(DateTimeOffset Start, DateTimeOffset End)
{
	public double Hours => (End - Start).TotalHours;
}

public sealed record Employee(string Id, string Name, IReadOnlyList<Shift> Shifts);

public sealed record Roster(IReadOnlyList<Employee> Employees);

// Per-employee constraints.
[Constraints]
public static partial class EmployeeConstraints
{
	public static readonly Constraint<Employee> MaxFortyHoursPerWeek = Constraints<Employee>.Check(emp =>
	{
		var byWeek = emp.Shifts.GroupBy(s => System.Globalization.ISOWeek.GetWeekOfYear(s.Start.DateTime));
		return [.. byWeek.Where(g => g.Sum(s => s.Hours) > 40).Select(g => Violation.Error($"Employee '{emp.Name}' exceeds 40 hours in week {g.Key}."))];
	});

	public static readonly Constraint<Employee> MinimumRestBetweenShifts = Constraints<Employee>.Check(emp =>
	{
		List<Shift> sorted = [.. emp.Shifts.OrderBy(s => s.Start)];
		return [
			.. sorted
				.Zip(sorted.Skip(1), (a, b) => (a, b))
				.Where(pair => (pair.b.Start - pair.a.End).TotalHours < 11)
				.Select(pair => Violation.Error($"Employee '{emp.Name}' has less than 11 hours rest between shifts."))
		];
	});
}

// Roster-level constraint that delegates down to per-employee rules.
[Constraints]
public static partial class RosterConstraints
{
	public static readonly Constraint<Roster> NoEmployeeOverFortyHours = Constraints<Roster>.Check(
		roster => [.. roster.Employees.SelectMany(emp => EmployeeConstraints.MaxFortyHoursPerWeek.Check(emp, emp))]
	);

	public static readonly Constraint<Roster> AllEmployeesHaveMinimumRest = Constraints<Roster>.Check(
		roster => [.. roster.Employees.SelectMany(emp => EmployeeConstraints.MinimumRestBetweenShifts.Check(emp, emp))]
	);
}
