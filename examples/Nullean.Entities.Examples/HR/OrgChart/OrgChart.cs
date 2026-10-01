namespace Nullean.Entities.Examples.HR.OrgChart;

public enum EmploymentStatus
{
	Active,
	OnLeave,
	Terminated
}

public sealed record Person(string Id, string Name, string? ManagerId, EmploymentStatus Status, int Level, IReadOnlyList<string> DirectReportIds)
{
	public Person AssignManager(string managerId) => this with { ManagerId = managerId };

	public Person AddReport(string reportId) => this with { DirectReportIds = [.. DirectReportIds, reportId] };

	public Person RemoveReport(string reportId) => this with { DirectReportIds = [.. DirectReportIds.Where(id => id != reportId)] };

	public Person Terminate() => this with { Status = EmploymentStatus.Terminated, DirectReportIds = [], ManagerId = null };
}

public sealed record OrgChart(IReadOnlyList<Person> People)
{
	public Person? Get(string id) => People.FirstOrDefault(p => p.Id == id);

	public OrgChart AddPerson(Person person) => this with { People = [.. People, person] };

	public OrgChart UpdatePerson(Person person) => this with { People = [.. People.Select(p => p.Id == person.Id ? person : p)] };
}

// Per-person constraints.
[Constraints]
public static partial class PersonConstraints
{
	public static readonly Constraint<Person> LegalStatusTransitions = Constraints<Person>
		.Transitions(p => p.Status)
		.Allow(EmploymentStatus.Active, EmploymentStatus.OnLeave)
		.Allow(EmploymentStatus.Active, EmploymentStatus.Terminated)
		.Allow(EmploymentStatus.OnLeave, EmploymentStatus.Active)
		.Allow(EmploymentStatus.OnLeave, EmploymentStatus.Terminated);

	public static readonly Constraint<Person> TerminatedHasNoReports = Constraints<Person>
		.When(p => p.Status == EmploymentStatus.Terminated)
		.Requires(p => p.DirectReportIds.Count == 0, "Terminated employees must not have direct reports.");

	public static readonly Constraint<Person> TerminatedHasNoManager = Constraints<Person>
		.When(p => p.Status == EmploymentStatus.Terminated)
		.Requires(p => p.ManagerId is null, "Terminated employees must not have a manager.");
}

// Org-level constraints that check graph integrity.
[Constraints]
public static partial class OrgChartConstraints
{
	public static readonly Constraint<OrgChart> NoCycles = Constraints<OrgChart>.Check(org =>
	{
		var index = org.People.ToDictionary(p => p.Id);
		var violations = new List<Violation>();
		foreach (var person in org.People.Where(p => p.ManagerId is not null))
		{
			var visited = new HashSet<string>();
			var current = person.Id;
			while (index.TryGetValue(current, out var p) && p.ManagerId is not null)
			{
				if (!visited.Add(current))
				{
					violations.Add(Violation.Error($"Cycle detected involving '{person.Name}'."));
					break;
				}
				current = p.ManagerId;
			}
		}
		return violations;
	});

	public static readonly Constraint<OrgChart> ManagersMustExist = Constraints<OrgChart>.Check(org =>
	{
		var ids = org.People.Select(p => p.Id).ToHashSet();
		return [
			.. org
				.People
				.Where(p => p.ManagerId is not null && !ids.Contains(p.ManagerId))
				.Select(p => Violation.Error($"Manager '{p.ManagerId}' of '{p.Name}' does not exist in the org."))
		];
	});
}
