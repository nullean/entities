using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore.Tests;

public sealed partial record SignUp(string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<SignUp> HasEmail = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> Adult = Constraints<SignUp>.Check(s => s.Age >= 18 ? [] : [Violation.Error(nameof(Age), "must be 18 or older")]);
}

public class EndpointTests
{
	private static async Task<(WebApplication App, HttpClient Client)> Host(Action<WebApplication> map, Action<IServiceCollection>? services = null)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddConstraints<SignUp>();
		services?.Invoke(builder.Services);
		var app = builder.Build();
		map(app);
		await app.StartAsync();
		return (app, app.GetTestClient());
	}

	private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
	{
		using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return doc.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
	}

	[Test]
	public async Task ValidateWith_rejects_with_a_400_keyed_by_member_and_lets_a_valid_body_through()
	{
		var (app, client) = await Host(a => a.MapPost("/signup", (SignUp s) => TypedResults.Ok(s.Email)).ValidateWith<RouteHandlerBuilder, SignUp>());
		await using var _ = app;

		var bad = await client.PostAsJsonAsync("/signup", new { email = "nope", age = 3 });
		bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await Errors(bad)).Keys.Should().BeEquivalentTo("Email", "Age");

		(await client.PostAsJsonAsync("/signup", new { email = "a@b.c", age = 30 })).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task A_group_filter_validates_every_registered_argument_type()
	{
		var (app, client) = await Host(a =>
		{
			var group = a.MapGroup("/api").ValidateConstraints();
			group.MapPost("/signup", (SignUp s) => "ok");
		});
		await using var _ = app;

		var bad = await client.PostAsJsonAsync("/api/signup", new { email = "nope", age = 30 });
		bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await Errors(bad)).Keys.Should().BeEquivalentTo("Email");
	}

	[Test]
	public async Task The_exception_handler_answers_ApplyOrThrow_with_a_400()
	{
		var (app, client) = await Host(
			a =>
			{
				a.UseExceptionHandler();
				a.MapPost("/signup", (SignUp s) => SignUp.Constraints.ApplyOrThrow(s, x => x with { Age = 1 }));
			},
			s => s.AddConstraintProblemDetails()
		);
		await using var _ = app;

		var response = await client.PostAsJsonAsync("/signup", new { email = "a@b.c", age = 30 });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await Errors(response)).Keys.Should().BeEquivalentTo("Age");
	}

	[Test]
	public async Task Result_and_ApplyOutcome_map_to_http_results()
	{
		var (app, client) = await Host(a =>
		{
			a.MapPost("/result", (SignUp s) => SignUp.Constraints.Apply(s, x => x with { Age = 1 }).ToHttpResult());
			a.MapPost("/outcome", (SignUp s) => SignUp.Constraints.ApplyDetailed(s, x => x with { Age = x.Age + 1 }).ToHttpResult());
		});
		await using var _ = app;
		var valid = new { email = "a@b.c", age = 30 };

		(await client.PostAsJsonAsync("/result", valid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await client.PostAsJsonAsync("/outcome", valid)).StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
