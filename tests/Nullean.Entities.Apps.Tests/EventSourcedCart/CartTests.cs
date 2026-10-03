using AwesomeAssertions;
using CsCheck;
using EventSourcedCart;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.Apps.Tests.EventSourcedCart;

// Event sourcing makes the model testable on its own: Decide and Evolve are pure, so every cart that can ever exist can be enumerated,
// and the rules are the assertions. Then the service is checked against a stream, including the one with a wrong decider.

public class CartTests
{
	private static IReadOnlyList<CartCommand> Commands() =>
		global::Nullean.Entities.CsCheck.Commands.Of<CartCommand>(
			global::Nullean.Entities.CsCheck.Commands.Over(q => new CartCommand.AddItem("apple", q), 1, 2),
			global::Nullean.Entities.CsCheck.Commands.Over(q => new CartCommand.AddItem("pear", q), 1, 3),
			[new CartCommand.RemoveItem("apple"), new CartCommand.RemoveItem("pear"), new CartCommand.Checkout()]
		);

	private static EventSourcedSpec<Cart, CartCommand, CartEvent> Spec(Func<Cart, CartCommand, IEnumerable<CartEvent>> decide) =>
		EntitySpec.EventSourced(Cart.Empty, decide, CartLogic.Evolve, Commands(), CartRules.All);

	[Test]
	public void Every_cart_that_can_be_reached_satisfies_every_rule()
	{
		var report = Spec(CartLogic.Decide).Spec.Key(c => $"{string.Join(",", c.Lines)}|{c.CheckedOut}").Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().BeGreaterThan(10);
	}

	[Test]
	public void A_decider_that_forgets_the_per_item_limit_is_found_with_the_commands_that_break_it()
	{
		static IEnumerable<CartEvent> Careless(Cart cart, CartCommand command) =>
			command is CartCommand.AddItem(var sku, var qty) && Catalog.Sells(sku) && qty > 0
				? [new CartEvent.ItemAdded(sku, qty)]
				: CartLogic.Decide(cart, command);

		var act = () => Spec(Careless).Spec.Key(c => $"{string.Join(",", c.Lines)}|{c.CheckedOut}").Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("quantities-are-within-limits");
	}

	[Test]
	public async Task The_service_refuses_what_the_rules_forbid_and_appends_nothing()
	{
		var store = new InMemoryEventStore();
		var careless = new CartService(store, (_, command) => command is CartCommand.AddItem(var sku, var qty) ? [new CartEvent.ItemAdded(sku, qty)] : []);
		var id = Guid.NewGuid();

		var result = await careless.HandleAsync(id, new CartCommand.AddItem("fig", 9));

		result.ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("quantities-are-within-limits");
		(await store.LoadAsync(id)).Events.Should().BeEmpty();
	}

	[Test]
	public async Task The_cart_is_whatever_the_stream_adds_up_to()
	{
		var service = new CartService(new InMemoryEventStore());
		var id = Guid.NewGuid();

		await service.HandleAsync(id, new CartCommand.AddItem("apple", 2));
		await service.HandleAsync(id, new CartCommand.AddItem("apple", 1));
		await service.HandleAsync(id, new CartCommand.RemoveItem("apple"));
		await service.HandleAsync(id, new CartCommand.AddItem("fig", 1));

		(await service.GetAsync(id)).Lines.Should().Equal(new CartLine("fig", 1));
	}

	[Test]
	public async Task Checkout_carries_the_total_and_freezes_the_cart()
	{
		var store = new InMemoryEventStore();
		var service = new CartService(store);
		var id = Guid.NewGuid();
		await service.HandleAsync(id, new CartCommand.AddItem("apple", 2));
		await service.HandleAsync(id, new CartCommand.AddItem("fig", 1));

		await service.HandleAsync(id, new CartCommand.Checkout());
		await service.HandleAsync(id, new CartCommand.AddItem("pear", 1));

		var (events, _) = await store.LoadAsync(id);
		events.OfType<CartEvent.CheckedOut>().Should().ContainSingle().Which.Total.Should().Be(3.00m);
		events.Should().HaveCount(3);
	}

	[Test]
	public async Task A_stale_append_is_refused()
	{
		var store = new InMemoryEventStore();
		var id = Guid.NewGuid();
		(await store.AppendAsync(id, 0, [new CartEvent.ItemAdded("fig", 1)])).Should().BeTrue();

		(await store.AppendAsync(id, 0, [new CartEvent.ItemAdded("fig", 1)])).Should().BeFalse();
	}

	[Test]
	public void Whatever_commands_arrive_the_cart_the_stream_adds_up_to_is_valid()
	{
		var command = Gen.OneOfConst(Commands().ToArray());

		command
			.List[0, 25]
			.Sample(
				commands =>
				{
					var service = new CartService(new InMemoryEventStore());
					var id = Guid.NewGuid();
					foreach (var c in commands)
					{
						service.HandleAsync(id, c).GetAwaiter().GetResult();
					}

					CartRules.All.Validate(service.GetAsync(id).GetAwaiter().GetResult()).Where(v => v.Severity == Severity.Error).Should().BeEmpty();
				},
				iter: 300
			);
	}
}
