using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities.Recipes.Mediator;
using Nullean.Entities.Recipes.Wolverine;
using Wolverine;
using Host = Microsoft.Extensions.Hosting.Host;

namespace Nullean.Entities.Recipes.Tests;

/// <summary>Backs docs/integration/mediators.md: each pipeline rejects an invalid request before its handler and passes a valid one through.</summary>
public class MediatorPipelineTests
{
	private static ServiceProvider Mediator() => new ServiceCollection().AddMediatorWithConstraints().BuildServiceProvider();

	[Test]
	public async Task Mediator_rejects_an_invalid_command_before_the_handler()
	{
		await using var sp = Mediator();
		var send = async () => await sp.GetRequiredService<global::Mediator.IMediator>().Send(new PlaceOrder("BOOK-1", 0));

		var thrown = await send.Should().ThrowAsync<ConstraintViolationException>();
		thrown.Which.Violations.Should().ContainSingle().Which.Member.Should().Be("Quantity");
	}

	[Test]
	public async Task Mediator_handles_a_valid_command()
	{
		await using var sp = Mediator();
		var reply = await sp.GetRequiredService<global::Mediator.IMediator>().Send(new PlaceOrder("BOOK-1", 2));
		reply.Should().Be("placed 2 x BOOK-1");
	}

	[Test]
	public async Task Wolverine_rejects_an_invalid_message_before_the_handler_and_handles_a_valid_one()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.AddWolverineWithConstraints();
		using var host = builder.Build();
		await host.StartAsync();
		try
		{
			var bus = host.Services.GetRequiredService<IMessageBus>();

			var invoke = () => bus.InvokeAsync<string>(new ShipParcel("1011 AB", 31m));
			var thrown = await invoke.Should().ThrowAsync<ConstraintViolationException>();
			thrown.Which.Violations.Should().ContainSingle().Which.Member.Should().Be("WeightKg");

			var reply = await bus.InvokeAsync<string>(new ShipParcel("1011 AB", 2.5m));
			reply.Should().Be("shipping 2.5 kg to 1011 AB");
		}
		finally
		{
			await host.StopAsync();
		}
	}
}
