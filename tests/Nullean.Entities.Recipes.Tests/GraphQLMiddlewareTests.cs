using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Recipes.GraphQL;

namespace Nullean.Entities.Recipes.Tests;

/// <summary>Backs docs/integration/graphql.md: HotChocolate's request executor, with an invalid input reported as a GraphQL error carrying member and code.</summary>
public class GraphQLMiddlewareTests
{
	private static async Task<JsonElement> Execute(string query)
	{
		var services = new ServiceCollection();
		services.AddGraphQLWithConstraints();
		await using var sp = services.BuildServiceProvider();
		var executor = await sp.GetRequestExecutorAsync();
		var result = await executor.ExecuteAsync(query);
		return JsonDocument.Parse(result.ToJson()).RootElement.Clone();
	}

	[Test]
	public async Task An_invalid_input_object_becomes_a_GraphQL_error_with_member_and_code()
	{
		var json = await Execute("""mutation { addBook(input: { title: "Dune", pages: 0 }) { title } }""");

		var error = json.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
		error.GetProperty("message").GetString().Should().Be("must be at least 1");
		error.GetProperty("path")[0].GetString().Should().Be("addBook");
		error.GetProperty("extensions").GetProperty("member").GetString().Should().Be("Pages");
		error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("has-pages");
		json.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Test]
	public async Task A_valid_input_object_runs_the_resolver()
	{
		var json = await Execute("""mutation { addBook(input: { title: "Dune", pages: 412 }) { title pages } }""");

		json.TryGetProperty("errors", out _).Should().BeFalse();
		json.GetProperty("data").GetProperty("addBook").GetProperty("pages").GetInt32().Should().Be(412);
	}
}
