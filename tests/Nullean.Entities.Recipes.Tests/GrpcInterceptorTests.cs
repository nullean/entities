using AwesomeAssertions;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Nullean.Entities.Recipes.Grpc;

namespace Nullean.Entities.Recipes.Tests;

/// <summary>Backs docs/integration/grpc.md: a real gRPC call over TestServer, rejected with INVALID_ARGUMENT and a google.rpc.BadRequest.</summary>
public class GrpcInterceptorTests
{
	private static async Task<(WebApplication App, SignUps.SignUpsClient Client)> Start()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddGrpcWithConstraints();
		var app = builder.Build();
		app.MapSignUps();
		await app.StartAsync();
		var channel = GrpcChannel.ForAddress(app.GetTestServer().BaseAddress, new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
		return (app, new SignUps.SignUpsClient(channel));
	}

	[Test]
	public async Task An_invalid_request_is_rejected_with_InvalidArgument_and_the_violations_as_BadRequest()
	{
		var (app, client) = await Start();
		await using var _ = app;

		var call = async () => await client.RegisterAsync(new SignUpRequest { Email = "not-an-email", Age = 30 });

		var thrown = await call.Should().ThrowAsync<RpcException>();
		thrown.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
		var badRequest = thrown.Which.GetRpcStatus()?.GetDetail<BadRequest>();
		badRequest.Should().NotBeNull();
		badRequest.FieldViolations.Should().ContainSingle().Which.Field.Should().Be("Email");
	}

	[Test]
	public async Task A_valid_request_reaches_the_service()
	{
		var (app, client) = await Start();
		await using var _ = app;

		var reply = await client.RegisterAsync(new SignUpRequest { Email = "ada@example.com", Age = 30 });
		reply.Id.Should().Be("user:ada@example.com");
	}
}
