using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nullean.Entities.EntityFrameworkCore.Tests;

// Real EF Core on SQLite in memory: the connection stays open for the test, so the database lives as long as it does.
public sealed class SaveChangesTests : IDisposable
{
	private readonly SqliteConnection _connection = new("DataSource=:memory:");
	private readonly List<(string Entity, IReadOnlyList<Violation> Violations)> _notices = [];

	public SaveChangesTests()
	{
		_connection.Open();
		using var db = NewContext();
		db.Database.EnsureCreated();
	}

	public void Dispose() => _connection.Dispose();

	private ShopContext NewContext() =>
		new(
			new DbContextOptionsBuilder<ShopContext>()
				.UseSqlite(_connection)
				.UseConstraints(c =>
				{
					c.Add<Order>();
					c.OnNotices = (entry, violations) => _notices.Add((entry.Metadata.DisplayName(), violations));
				})
				.Options
		);

	private int SeedPlacedOrder()
	{
		using var db = NewContext();
		var order = new Order { Customer = "ada", Total = 10, Status = OrderStatus.Placed, Version = 3 };
		db.Orders.Add(order);
		db.SaveChanges();
		return order.Id;
	}

	private int CountOrders()
	{
		using var db = NewContext();
		return db.Orders.Count();
	}

	[Test]
	public async Task Added_invalid_entity_throws_and_nothing_is_saved()
	{
		using var db = NewContext();
		db.Orders.Add(new Order { Customer = "ada", Total = -1 });
		db.Orders.Add(new Order { Customer = "", Total = 5 });

		var act = () => db.SaveChanges();

		var ex = act.Should().Throw<ConstraintViolationException>().Which;
		ex.Violations.Select(v => v.Member).Should().BeEquivalentTo(["Total", "Customer"]);
		ex.Message.Should().Contain("Order (Added)");
		await Task.CompletedTask;
		CountOrders().Should().Be(0);
	}

	[Test]
	public async Task Added_invalid_entity_throws_on_the_async_path()
	{
		await using var db = NewContext();
		db.Orders.Add(new Order { Customer = "ada", Total = -1 });

		var act = async () => await db.SaveChangesAsync();

		(await act.Should().ThrowAsync<ConstraintViolationException>()).Which.Violations.Should().ContainSingle(v => v.Member == "Total");
		CountOrders().Should().Be(0);
	}

	[Test]
	public async Task Valid_entities_are_saved()
	{
		await using var db = NewContext();
		db.Orders.Add(new Order { Customer = "ada", Total = 10 });
		db.Notes.Add(new Note { Text = "" });

		(await db.SaveChangesAsync()).Should().Be(2);
		CountOrders().Should().Be(1);
	}

	[Test]
	public async Task Modified_entity_breaking_a_transition_throws_and_the_row_is_unchanged()
	{
		var id = SeedPlacedOrder();
		using (var db = NewContext())
		{
			var order = db.Orders.Single(o => o.Id == id);
			order.Status = OrderStatus.Draft; // Placed -> Draft is not an allowed edge.

			var act = () => db.SaveChanges();

			var ex = act.Should().Throw<ConstraintViolationException>().Which;
			ex.Violations.Should().ContainSingle(v => v.ConstraintId == "status-flow");
			ex
				.Message
				.Should()
				.Be("SaveChanges rejected, constraints violated. Order (Modified): status-flow does not allow the transition from Placed to Draft.");
		}
		using var check = NewContext();
		check.Orders.Single(o => o.Id == id).Status.Should().Be(OrderStatus.Placed);
		await Task.CompletedTask;
	}

	[Test]
	public async Task Modified_entity_breaking_a_monotonic_rule_throws_on_the_async_path()
	{
		var id = SeedPlacedOrder();
		await using var db = NewContext();
		var order = await db.Orders.SingleAsync(o => o.Id == id);
		order.Version = 2; // was 3

		var act = async () => await db.SaveChangesAsync();

		(await act.Should().ThrowAsync<ConstraintViolationException>()).Which.Violations.Should()
			.ContainSingle(v => v.ConstraintId == "version-only-grows");
	}

	[Test]
	public async Task Modified_entity_with_an_allowed_transition_is_saved()
	{
		var id = SeedPlacedOrder();
		await using (var db = NewContext())
		{
			var order = await db.Orders.SingleAsync(o => o.Id == id);
			order.Status = OrderStatus.Shipped;
			order.Version = 4;
			(await db.SaveChangesAsync()).Should().Be(1);
		}
		await using var check = NewContext();
		(await check.Orders.SingleAsync(o => o.Id == id)).Status.Should().Be(OrderStatus.Shipped);
	}

	[Test]
	public async Task Modified_entity_breaking_a_point_in_time_rule_throws()
	{
		var id = SeedPlacedOrder();
		await using var db = NewContext();
		var order = await db.Orders.SingleAsync(o => o.Id == id);
		order.Total = -5;

		var act = async () => await db.SaveChangesAsync();

		(await act.Should().ThrowAsync<ConstraintViolationException>()).Which.Violations.Should().ContainSingle(v => v.Member == "Total");
	}

	[Test]
	public async Task Warnings_do_not_block_and_reach_OnNotices()
	{
		await using var db = NewContext();
		db.Orders.Add(new Order { Customer = "ada", Total = 20_000 });

		(await db.SaveChangesAsync()).Should().Be(1);

		_notices.Should().ContainSingle();
		_notices[0].Entity.Should().Be("Order");
		_notices[0].Violations.Should().ContainSingle(v => v.Severity == Severity.Warning);
	}

	[Test]
	public async Task Deleted_entities_are_not_checked()
	{
		var id = SeedPlacedOrder();
		await using var db = NewContext();
		var order = await db.Orders.SingleAsync(o => o.Id == id);
		order.Total = -5; // would fail if it were checked
		db.Orders.Remove(order);

		(await db.SaveChangesAsync()).Should().Be(1);
		CountOrders().Should().Be(0);
	}
}
