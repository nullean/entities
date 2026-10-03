using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using OrgChart;
using static Apps.Narrator;

// A reporting hierarchy. Run it with `dotnet run`.

await using var provider = new ServiceCollection().AddOrgChart().BuildServiceProvider();
var org = provider.GetRequiredService<OrgService>();
var repository = provider.GetRequiredService<IOrgRepository>();

static string Why(Result<Org> result) => string.Join("; ", result.ViolationsOrEmpty.Select(v => v.Message));

async Task<string> Chart()
{
	var head = (await repository.LoadAsync()).Head;
	IEnumerable<OrgUnit> roots = head is null ? [] : [head];
	return string.Join(
		"\n   ",
		Tree.FlattenWithAncestors(roots, u => u.Reports).Select(n => $"{new string(' ', 2 * n.Ancestors.Count)}{n.Node.Name} ({n.Node.Id})")
	);
}

Section("Building the hierarchy");
await org.FoundAsync("Grace");
await org.HireAsync("Alan", managerId: 1); // 2
await org.HireAsync("Edsger", managerId: 1); // 3
await org.HireAsync("Barbara", managerId: 2); // 4
await org.HireAsync("Donald", managerId: 2); // 5
await org.HireAsync("Ken", managerId: 4); // 6
Say(await Chart());
Expect((await repository.LoadAsync()).Count == 6, "six people");

Section("A hire under a manager who does not exist");
var ghost = await org.HireAsync("Nobody", managerId: 99);
Say($"-> {Why(ghost)}");
Expect(ghost.IsFail && (await repository.LoadAsync()).Count == 6, "refused, and nothing changed");

Section("Moving someone under their own team would lose them");
var loop = await org.MoveAsync(id: 2, newManagerId: 6);
Say($"Alan under Ken (who reports to Alan's report) -> {Why(loop)}");
Expect(loop.IsFail, "a manager cannot be moved under their own subordinate");

var fine = await org.MoveAsync(id: 6, newManagerId: 3);
Expect(fine.IsOk && (await repository.LoadAsync()).Count == 6, "moving Ken under Edsger keeps all six");

Section("Limits the shape must respect");
for (var i = 0; i < 7; i++) // Edsger already has Ken: eight reports in all

{
	await org.HireAsync($"Report {i}", managerId: 3);
}
var tooMany = await org.HireAsync("One too many", managerId: 3);
Say($"a ninth report for Edsger -> {Why(tooMany)}");
Expect(tooMany.IsFail, "nobody has more than eight direct reports");

var deepest = (await repository.LoadAsync()).People.First(p => p.Name == "Ken").Id;
for (var level = 0; level < 3; level++)
{
	deepest = (await org.HireAsync($"Layer {level}", managerId: deepest)).Unwrap().NextId - 1;
}
var tooDeep = await org.HireAsync("Too deep", managerId: deepest);
Say($"one level too many -> {Why(tooDeep)}");
Expect(tooDeep.IsFail, "the chain of command is at most six levels deep");

Section("Terminating promotes the reports; the head cannot be replaced by an edit");
var before = (await repository.LoadAsync()).Count;
Expect((await org.TerminateAsync(2)).IsOk, "Alan leaves; Barbara and Donald now report to Grace");
Expect((await repository.LoadAsync()).Count == before - 1, "exactly one person fewer");
var head = await org.TerminateAsync(1);
Say($"Grace leaves -> {Why(head)}");
Expect(head.IsFail, "removing the head would orphan everyone");

return Finish();
