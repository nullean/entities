using Microsoft.EntityFrameworkCore;

namespace Nullean.Entities.EntityFrameworkCore;

public static class ConstraintDbContextOptionsBuilderExtensions
{
	/// <summary>
	/// Adds a <see cref="ConstraintSaveChangesInterceptor"/> that checks the constraint sets registered in <paramref name="configure"/> on every <c>SaveChanges</c>
	/// and <c>SaveChangesAsync</c>: <c>options.UseConstraints(c =&gt; c.Add&lt;Order&gt;().Add(CustomerRules.All))</c>.
	/// </summary>
	public static DbContextOptionsBuilder UseConstraints(this DbContextOptionsBuilder optionsBuilder, Action<EntityConstraints> configure)
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		ArgumentNullException.ThrowIfNull(configure);
		var constraints = new EntityConstraints();
		configure(constraints);
		return optionsBuilder.AddInterceptors(new ConstraintSaveChangesInterceptor(constraints));
	}

	/// <inheritdoc cref="UseConstraints(DbContextOptionsBuilder, Action{EntityConstraints})"/>
	public static DbContextOptionsBuilder<TContext> UseConstraints<TContext>(
		this DbContextOptionsBuilder<TContext> optionsBuilder,
		Action<EntityConstraints> configure
	) where TContext : DbContext => (DbContextOptionsBuilder<TContext>)UseConstraints((DbContextOptionsBuilder)optionsBuilder, configure);
}
