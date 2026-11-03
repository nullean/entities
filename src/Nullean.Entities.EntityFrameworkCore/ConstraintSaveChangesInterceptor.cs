using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nullean.Entities.EntityFrameworkCore;

/// <summary>
/// Checks every added or modified entity whose type has a registered constraint set before <c>SaveChanges</c> writes anything, and throws a
/// <see cref="ConstraintViolationException"/> carrying the violations of every offending entity when any rule reports an error.
/// Deleted and unchanged entities are not checked.
/// </summary>
public sealed class ConstraintSaveChangesInterceptor(EntityConstraints constraints) : SaveChangesInterceptor
{
	public EntityConstraints Constraints { get; } = constraints ?? throw new ArgumentNullException(nameof(constraints));

	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		ThrowOnViolations(eventData.Context);
		return result;
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default
	)
	{
		ThrowOnViolations(eventData.Context);
		return ValueTask.FromResult(result);
	}

	private void ThrowOnViolations(DbContext? context)
	{
		if (context is null)
		{
			return;
		}
		List<Violation>? all = null;
		List<string>? offenders = null;
		List<(EntityEntry Entry, IReadOnlyList<Violation> Violations)>? notices = null;
		// Entries() runs DetectChanges first (unless AutoDetectChangesEnabled is off), so an entity mutated in place is already Modified here.
		foreach (var entry in context.ChangeTracker.Entries())
		{
			if (entry.State is not (EntityState.Added or EntityState.Modified))
			{
				continue;
			}
			var violations = Constraints.Check(entry);
			if (violations.Count == 0)
			{
				continue;
			}
			if (violations.Any(v => v.Severity == Severity.Error))
			{
				(all ??= []).AddRange(violations);
				(offenders ??= []).Add($"{entry.Metadata.DisplayName()} ({entry.State}): {string.Join(", ", violations.Select(v => v.Message))}");
			}
			else
			{
				(notices ??= []).Add((entry, violations));
			}
		}
		if (all is not null)
		{
			throw new ConstraintViolationException(all, $"SaveChanges rejected, constraints violated. {string.Join("; ", offenders!)}");
		}
		if (notices is not null && Constraints.OnNotices is { } onNotices)
		{
			foreach (var (entry, violations) in notices)
			{
				onNotices(entry, violations);
			}
		}
	}
}
