using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.Grpc;

public static class GrpcSetup
{
	public static IServiceCollection AddGrpcWithConstraints(this IServiceCollection services)
	{
		services.AddConstraints(SignUpRequestRules.All);
		services.AddGrpc(o => o.Interceptors.Add<ConstraintInterceptor>());
		return services;
	}

	public static WebApplication MapSignUps(this WebApplication app)
	{
		app.MapGrpcService<SignUpService>();
		return app;
	}
}
