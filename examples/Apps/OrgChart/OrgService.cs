using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace OrgChart;

public interface IOrgRepository
{
	Task<Org> LoadAsync(CancellationToken cancellationToken = default);

	Task SaveAsync(Org org, CancellationToken cancellationToken = default);
}

public sealed class InMemoryOrgRepository : IOrgRepository
{
	private Org _org = Org.Empty;

	public Task<Org> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volatile.Read(ref _org));

	public Task SaveAsync(Org org, CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref _org, org);
		return Task.CompletedTask;
	}
}

/// <summary>
/// The service performs the edit and hands it to the rule set for that kind of edit. It never inspects the tree.
/// One thing it does decide: <c>Apply</c> treats an edit that leaves the value unchanged as a no-op, <c>Ok</c> with no rule run. For "found the company" a repeat is
/// harmless, but for a hire, a move or a termination, "nothing happened" means the edit named someone who is not there, so the service reports it with <c>ApplyDetailed</c>.
/// </summary>
public sealed class OrgService(IOrgRepository repository)
{
	public Task<Result<Org>> FoundAsync(string name, CancellationToken cancellationToken = default) =>
		ApplyAsync(OrgRules.All, org => org.Found(name), nothingChangedIsAnError: false, cancellationToken);

	public Task<Result<Org>> HireAsync(string name, int managerId, CancellationToken cancellationToken = default) =>
		ApplyAsync(OrgRules.ForHire, org => org.Hire(org.NextId, name, managerId), nothingChangedIsAnError: true, cancellationToken);

	public Task<Result<Org>> MoveAsync(int id, int newManagerId, CancellationToken cancellationToken = default) =>
		ApplyAsync(OrgRules.ForMove, org => org.Move(id, newManagerId), nothingChangedIsAnError: true, cancellationToken);

	public Task<Result<Org>> TerminateAsync(int id, CancellationToken cancellationToken = default) =>
		ApplyAsync(OrgRules.ForTerminate, org => org.Terminate(id), nothingChangedIsAnError: true, cancellationToken);

	private async Task<Result<Org>> ApplyAsync(ConstraintSet<Org> rules, Func<Org, Org> edit, bool nothingChangedIsAnError, CancellationToken cancellationToken)
	{
		var org = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
		switch (rules.ApplyDetailed(org, edit))
		{
			case ApplyOutcome<Org>.Applied applied:
				await repository.SaveAsync(applied.Value, cancellationToken).ConfigureAwait(false);
				return new Result<Org>.Ok(applied.Value);
			case ApplyOutcome<Org>.Failure failure:
				return new Result<Org>.Fail(failure.Attempted, failure.Violations);
			default:
				return nothingChangedIsAnError
					? new Result<Org>.Fail(org, [Violation.Error("Nothing changed: there is no such person to edit")])
					: new Result<Org>.Ok(org);
		}
	}
}

public static class OrgChartServices
{
	public static IServiceCollection AddOrgChart(this IServiceCollection services) =>
		services
			.AddSingleton<IOrgRepository, InMemoryOrgRepository>()
			.AddSingleton<OrgService>();
}
