using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nullean.Entities.EntityFrameworkCore.Tests;

public sealed class Basket
{
	public int Id { get; set; }
	public string Owner { get; set; } = "";
	public List<BasketLine> Lines { get; set; } = [];
}

public sealed class BasketLine
{
	public int Id { get; set; }
	public string Sku { get; set; } = "";
	public int BasketId { get; set; }
}

public sealed class BasketContext(DbContextOptions<BasketContext> options) : DbContext(options)
{
	public DbSet<Basket> Baskets => Set<Basket>();
}

// What a rule sees of navigations: the current entity has what is loaded; the "before" built from OriginalValues.ToObject() has scalar properties only.
public sealed class NavigationTests
{
	private static readonly ConstraintSet<Basket> BasketRules = ConstraintSet.From(
		("UniqueSkus",
		Constraints<Basket>.Check(b =>
				b.Lines.GroupBy(l => l.Sku).Any(g => g.Count() > 1) ? [Violation.Error(nameof(Basket.Lines), "skus must be unique")] : []
		))
	);

	[Test]
	public async Task A_rule_over_a_loaded_collection_sees_it_and_the_before_has_no_navigations()
	{
		using var connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var options = new DbContextOptionsBuilder<BasketContext>().UseSqlite(connection).UseConstraints(c => c.Add(BasketRules)).Options;
		await using (var seed = new BasketContext(options))
		{
			await seed.Database.EnsureCreatedAsync();
			seed.Baskets.Add(new Basket { Owner = "ada", Lines = [new BasketLine { Sku = "a" }] });
			await seed.SaveChangesAsync();
		}

		await using var db = new BasketContext(options);
		var basket = await db.Baskets.Include(b => b.Lines).SingleAsync();
		basket.Owner = "grace";
		var entry = db.Entry(basket);
		((Basket)entry.OriginalValues.ToObject()).Lines.Should().BeEmpty();
		basket.Lines.Should().HaveCount(1);

		// A duplicate added to the loaded collection: the owner is Modified, so its rules run over the in-memory Lines.
		basket.Lines.Add(new BasketLine { Sku = "a" });
		var act = async () => await db.SaveChangesAsync();
		(await act.Should().ThrowAsync<ConstraintViolationException>()).Which.Violations.Should().ContainSingle(v => v.Member == "Lines");
	}

	// The pitfall: adding a child does not modify its parent, so the parent is Unchanged and its rules do not run.
	[Test]
	public async Task Adding_only_a_child_leaves_the_parent_unchecked()
	{
		using var connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var options = new DbContextOptionsBuilder<BasketContext>().UseSqlite(connection).UseConstraints(c => c.Add(BasketRules)).Options;
		await using (var seed = new BasketContext(options))
		{
			await seed.Database.EnsureCreatedAsync();
			seed.Baskets.Add(new Basket { Owner = "ada", Lines = [new BasketLine { Sku = "a" }] });
			await seed.SaveChangesAsync();
		}

		await using var db = new BasketContext(options);
		var basket = await db.Baskets.Include(b => b.Lines).SingleAsync();
		basket.Lines.Add(new BasketLine { Sku = "a" });

		(await db.SaveChangesAsync()).Should().Be(1);
		db.Entry(basket).State.Should().Be(EntityState.Unchanged);
	}
}
