using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.MediatR;

public static class MediatRSetup
{
	public static IServiceCollection AddMediatRWithConstraints(this IServiceCollection services, string? licenseKey = null) =>
		services
			.AddConstraints<RegisterUser>()
			.AddMediatR(cfg =>
			{
				cfg.LicenseKey = licenseKey; // required in production only; read it from configuration
				cfg.RegisterServicesFromAssemblyContaining<RegisterUserHandler>();
				// curb formats an open generic as `<, >`, which IDE0055 then rejects
#pragma warning disable IDE0055
				cfg.AddOpenBehavior(typeof(ConstraintBehavior<, >));
#pragma warning restore IDE0055
			});
}
