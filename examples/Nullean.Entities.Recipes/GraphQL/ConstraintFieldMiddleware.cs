// HotChocolate 16: a field middleware that validates every argument whose CLR type was registered with AddConstraints (an input object bound to a
// record), and reports each violation as a GraphQL error with extensions { member, code } instead of running the resolver.

using HotChocolate;
using HotChocolate.Resolvers;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.Recipes.GraphQL;

public static class ConstraintFieldMiddleware
{
	public static FieldMiddleware Create(IConstraintRegistry registry) =>
		next => async context =>
		{
			var failed = false;
			foreach (var argument in context.Selection.Field.Arguments)
			{
				if (context.ArgumentValue<object?>(argument.Name) is not { } value || !registry.TryValidate(value, out var violations))
				{
					continue;
				}
				foreach (var v in violations.Where(v => v.Severity == Severity.Error))
				{
					failed = true;
					context.ReportError(
						ErrorBuilder
							.New()
							.SetMessage(v.Message)
							.SetPath(context.Path)
							.SetExtension("member", v.Member)
							.SetExtension("code", v.ConstraintId)
							.Build()
					);
				}
			}
			if (!failed)
			{
				await next(context).ConfigureAwait(false);
			}
		};
}

public sealed partial record AddBookInput(string Title, int Pages)
{
	[Constraint]
	public static readonly Constraint<AddBookInput> HasPages = Constraints<AddBookInput>.Check(
		b => b.Pages > 0 ? [] : [Violation.Error(nameof(Pages), "must be at least 1")]
	);
}

public sealed record Book(string Title, int Pages);

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "HotChocolate binds instance members")]
public sealed class Query
{
	public string Ping() => "pong";
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "HotChocolate binds instance members")]
public sealed class Mutation
{
	public Book AddBook(AddBookInput input) => new(input.Title, input.Pages);
}
