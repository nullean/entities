using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Nullean.Entities.AspNetCore.Tests;

// [Constraints(ValidatableObject = true)] behind the framework's own minimal API validation (builder.Services.AddValidation(), Microsoft.Extensions.Validation),
// with none of this package's filters. The docs page docs/integration/data-annotations.md shows this type.
[Constraints(ValidatableObject = true)]
public sealed partial class Booking
{
	[Required]
	public string? Guest { get; set; }

	[Range(1, 10)]
	public int Nights { get; set; }

	public DateOnly From { get; set; }
	public DateOnly To { get; set; }

	public static readonly Constraint<Booking> EndsAfterItStarts = Constraints<Booking>.Always(
		b => b,
		b => b.To > b.From,
		"To must be after From",
		member: "To"
	);
}

// No DataAnnotations attribute at all: the only validation is the generated IValidatableObject.
[Constraints(ValidatableObject = true)]
public sealed partial class Transfer : IValidatableObject
{
	public decimal Amount { get; set; }

	public static readonly Constraint<Transfer> Positive = Constraints<Transfer>.Always(t => t.Amount, a => a > 0, "Amount must be positive");

	// Not about one member: AddValidation keys it under "".
	public static readonly Constraint<Transfer> NotTooLarge = Constraints<Transfer>.Always(t => t.Amount < 1_000_000, "Too large for one transfer");
}

public class ValidatableObjectEndpointTests
{
	private static async Task<(WebApplication App, HttpClient Client)> Host()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddValidation();
		var app = builder.Build();
		app.MapPost("/bookings", (Booking b) => TypedResults.Ok());
		app.MapPost("/transfers", (Transfer t) => TypedResults.Ok());
		await app.StartAsync();
		return (app, app.GetTestClient());
	}

	private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
	{
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return doc.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
	}

	[Test]
	public async Task AddValidation_reports_a_broken_constraint_once_keyed_by_its_member()
	{
		var (app, client) = await Host();
		await using var _ = app;

		var errors = await Errors(await client.PostAsJsonAsync("/bookings", new { guest = "Ada", nights = 2, from = "2026-01-02", to = "2026-01-01" }));

		errors.Should().ContainSingle();
		errors["To"].Should().Equal("To must be after From");
	}

	// AddValidation runs IValidatableObject only once every attribute on the type passes (checked by running it on .NET 10 and 11), so a request that
	// breaks an attribute and a constraint gets the attribute error first, once, and the constraint error on the next attempt, once.
	[Test]
	public async Task An_annotation_error_is_reported_once_and_the_constraint_follows_when_it_is_fixed()
	{
		var (app, client) = await Host();
		await using var _ = app;

		var first = await Errors(await client.PostAsJsonAsync("/bookings", new { guest = "Ada", nights = 0, from = "2026-01-02", to = "2026-01-01" }));
		first.Should().ContainSingle();
		first["Nights"].Should().Equal("The field Nights must be between 1 and 10.");

		var second = await Errors(await client.PostAsJsonAsync("/bookings", new { guest = "Ada", nights = 2, from = "2026-01-02", to = "2026-01-01" }));
		second.Should().ContainSingle();
		second["To"].Should().Equal("To must be after From");

		(await client.PostAsJsonAsync("/bookings", new { guest = "Ada", nights = 2, from = "2026-01-01", to = "2026-01-02" })).StatusCode.Should()
			.Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task A_type_with_no_attribute_is_still_validated()
	{
		var (app, client) = await Host();
		await using var _ = app;

		(await Errors(await client.PostAsJsonAsync("/transfers", new { amount = 0 })))["Amount"].Should().Equal("Amount must be positive");
		(await client.PostAsJsonAsync("/transfers", new { amount = 5 })).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Test]
	public async Task A_rule_that_is_not_about_one_member_is_keyed_by_an_empty_string()
	{
		var (app, client) = await Host();
		await using var _ = app;

		var errors = await Errors(await client.PostAsJsonAsync("/transfers", new { amount = 2_000_000 }));
		errors.Should().ContainSingle();
		errors[""].Should().Equal("Too large for one transfer");
	}
}
