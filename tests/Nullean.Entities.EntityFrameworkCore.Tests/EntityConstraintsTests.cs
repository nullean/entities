using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nullean.Entities.EntityFrameworkCore.Tests;

public class Vehicle
{
	public int Id { get; set; }
	public int Wheels { get; set; }
}

public sealed class Truck : Vehicle
{
	public int Axles { get; set; }
}

public sealed class FleetContext(DbContextOptions<FleetContext> options) : DbContext(options)
{
	public DbSet<Vehicle> Vehicles => Set<Vehicle>();
	public DbSet<Truck> Trucks => Set<Truck>();
}

public sealed class EntityConstraintsTests
{
	private static readonly ConstraintSet<Vehicle> VehicleRules = ConstraintSet.From(
		("HasWheels", Constraints<Vehicle>.Check(v => v.Wheels > 0 ? [] : [Violation.Error(nameof(Vehicle.Wheels), "must be positive")]))
	);

	[Test]
	public async Task A_set_registered_for_a_base_type_checks_derived_entities()
	{
		using var connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var options = new DbContextOptionsBuilder<FleetContext>().UseSqlite(connection).UseConstraints(c => c.Add(VehicleRules)).Options;
		await using var db = new FleetContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Trucks.Add(new Truck { Wheels = 0, Axles = 2 });

		var act = async () => await db.SaveChangesAsync();

		(await act.Should().ThrowAsync<ConstraintViolationException>()).Which.Violations.Should().ContainSingle(v => v.Member == "Wheels");
	}

	[Test]
	public async Task IsRegistered_follows_base_types()
	{
		var constraints = new EntityConstraints().Add(VehicleRules);

		constraints.IsRegistered(typeof(Truck)).Should().BeTrue();
		constraints.IsRegistered(typeof(Note)).Should().BeFalse();
		await Task.CompletedTask;
	}

	// The "before" handed to a sequential rule is the row as EF loaded it, not the mutated instance: OriginalValues.ToObject() builds a fresh copy.
	[Test]
	public async Task Check_compares_the_original_row_with_the_current_entity()
	{
		using var connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var constraints = new EntityConstraints().Add<Order>();
		var options = new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options;
		await using var db = new ShopContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Orders.Add(new Order { Customer = "ada", Total = 1, Status = OrderStatus.Shipped, Version = 1 });
		await db.SaveChangesAsync();

		var order = await db.Orders.SingleAsync();
		order.Status = OrderStatus.Draft;
		var entry = db.ChangeTracker.Entries<Order>().Single();

		entry.State.Should().Be(EntityState.Modified);
		constraints.Check(entry).Should().ContainSingle(v => v.ConstraintId == "status-flow");
	}
}
