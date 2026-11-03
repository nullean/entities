using Microsoft.Extensions.Hosting;
using Nullean.Entities.Extensions.DependencyInjection;
using Wolverine;

namespace Nullean.Entities.Recipes.Wolverine;

public static class WolverineSetup
{
	public static HostApplicationBuilder AddWolverineWithConstraints(this HostApplicationBuilder builder)
	{
		builder.Services.AddConstraints<ShipParcel>();
		builder.UseWolverine(opts =>
		{
			// Only this recipe's handler: the other recipes in this assembly have classes Wolverine's naming conventions would pick up too.
			opts.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ShipParcelHandler));
			opts.Policies.AddMiddleware(typeof(ConstraintMiddleware));
		});
		return builder;
	}
}
