using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.GraphQL;

public static class GraphQLSetup
{
	public static IRequestExecutorBuilder AddGraphQLWithConstraints(this IServiceCollection services) =>
		services
			.AddConstraints<AddBookInput>()
			.AddGraphQL()
			.AddQueryType<Query>()
			.AddMutationType<Mutation>()
			.UseField(next => context => ConstraintFieldMiddleware.Create(context.Services.GetRequiredService<IConstraintRegistry>())(next)(context));
}
