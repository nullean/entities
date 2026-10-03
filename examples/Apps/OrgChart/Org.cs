using Nullean.Entities;

namespace OrgChart;

public sealed record OrgUnit(int Id, string Name, IReadOnlyList<OrgUnit> Reports);

/// <summary>
/// A reporting hierarchy as a nested tree: one head, and everyone else somewhere below. The edits are plain tree surgery (<c>Tree.Map</c> rebuilds a tree bottom-up)
/// and do not check anything: an edit that goes wrong (a hire under a manager who does not exist) simply does not change the tree, and the rules say what that means.
/// </summary>
public sealed record Org(OrgUnit? Head)
{
	public static readonly Org Empty = new((OrgUnit?)null);

	public IEnumerable<OrgUnit> People => Head is null ? [] : Tree.Flatten([Head], u => u.Reports);

	public int Count => People.Count();

	public int NextId => People.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1;

	public Org Found(string name) => Head is null ? new Org(new OrgUnit(1, name, [])) : this;

	public Org Hire(int id, string name, int managerId) => Under(managerId, new OrgUnit(id, name, []));

	/// <summary>Moves a person, and everyone who reports to them, under another manager. Wrong targets lose the person, which is what a rule must catch.</summary>
	public Org Move(int id, int newManagerId) =>
		People.FirstOrDefault(p => p.Id == id) is { } moving ? Without(id).Under(newManagerId, moving) : this;

	/// <summary>Removes one person; their reports go to their manager. Nobody can take the place of the head, so removing the head with reports orphans them.</summary>
	public Org Terminate(int id)
	{
		if (Head is null)
		{
			return this;
		}
		if (Head.Id == id)
		{
			return new Org((OrgUnit?)null);
		}
		return new Org(
			Tree.Map<OrgUnit>(
				[Head],
				u => u.Reports,
				(u, kids) => u with { Reports = [.. kids.SelectMany(k => k.Id == id ? People.First(p => p.Id == id).Reports : [k])] }
			)[0]
		);
	}

	private Org Under(int managerId, OrgUnit unit) =>
		Head is null
			? this
			: new Org(Tree.Map<OrgUnit>([Head], u => u.Reports, (u, kids) => u with { Reports = u.Id == managerId ? [.. kids, unit] : kids })[0]);

	private Org Without(int id) =>
		Head is null || Head.Id == id
			? new Org((OrgUnit?)null)
			: new Org(Tree.Map<OrgUnit>([Head], u => u.Reports, (u, kids) => u with { Reports = [.. kids.Where(k => k.Id != id)] })[0]);
}

/// <summary>What must hold of a hierarchy at any moment. Each runs over every node of the tree (<c>Tree.Flatten</c>), however deep.</summary>
public static class OrgRules
{
	public const int MaxReports = 8;
	public const int MaxLevels = 6;

	public static readonly ConstraintSet<Org> All = ConstraintSet.From(
		("one-person-one-place", Constraints<Org>.Unique(o => o.People, p => p.Id, message: "A person appears more than once in the hierarchy")),
		("everyone-has-a-name", Constraints<Org>.Always(o => o.People.All(p => !string.IsNullOrWhiteSpace(p.Name)), "Everyone needs a name")),
		("span-of-control",
		Constraints<Org>.Always(o => o.People.All(p => p.Reports.Count <= MaxReports), $"Nobody has more than {MaxReports} direct reports")),
		("chain-of-command",
		Constraints<Org>.Always(
			o => o.Head is null || Tree.FlattenWithAncestors([o.Head], u => u.Reports).All(n => n.Ancestors.Count < MaxLevels),
			$"The chain of command is at most {MaxLevels} levels deep"
		))
	);

	// One rule per kind of edit, attached only to that edit (a rule that is right for Hire is wrong for Terminate).
	private static ConstraintSet<Org> With(string name, Constraint<Org> step) => ConstraintSet.Merge(All, ConstraintSet.From((name, step)));

	/// <summary>A hire adds exactly one person: a hire under a manager who does not exist changes nothing, and that is the failure.</summary>
	public static readonly ConstraintSet<Org> ForHire = With(
		"hire-adds-one-person",
		Constraints<Org>.Transitions((before, after) => after.Count == before.Count + 1, "The hire did not happen: is there such a manager?")
	);

	/// <summary>A move conserves the headcount: moving someone under their own subordinate loses them, and so does a manager who does not exist.</summary>
	public static readonly ConstraintSet<Org> ForMove = With(
		"move-keeps-everyone",
		Constraints<Org>.Conserves(o => o.Count, "The move would lose people: a manager cannot move under their own team, and the new manager must exist")
	);

	/// <summary>A termination removes exactly one person: removing the head would orphan everyone below.</summary>
	public static readonly ConstraintSet<Org> ForTerminate = With(
		"terminate-removes-one-person",
		Constraints<Org>.Transitions(
			(before, after) => after.Count == before.Count - 1,
			"Terminating this person would orphan others: appoint a new head first"
		)
	);
}
