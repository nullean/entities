using Microsoft.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.Mediator;

public static class MediatorSetup
{
	public static IServiceCollection AddMediatorWithConstraints(this IServiceCollection services) =>
		services.AddMediator(options =>
		{
			options.Namespace = "Nullean.Entities.Recipes.Mediator.Generated";
			// curb formats an open generic as `<, >`, which IDE0055 then rejects
#pragma warning disable IDE0055
			options.PipelineBehaviors = [typeof(ConstraintBehavior<,>)];
#pragma warning restore IDE0055
		});
}
