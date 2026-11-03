using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Nullean.Entities.EntityFrameworkCore.Tests;

// AddDbContext runs the options action once per scope. UseConstraints there must not make EF build a new internal service provider each time.
public sealed class DependencyInjectionTests
{
	[Test]
	public async Task UseConstraints_in_AddDbContext_checks_every_scope_without_new_service_providers()
	{
		using var connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var services = new ServiceCollection();
		services.AddDbContext<ShopContext>(options =>
				options
					.UseSqlite(connection)
					.UseConstraints(c => c.Add<Order>())
					.ConfigureWarnings(w => w.Throw(CoreEventId.ManyServiceProvidersCreatedWarning))
		);
		await using var provider = services.BuildServiceProvider();
		await using (var scope = provider.CreateAsyncScope())
		{
			await scope.ServiceProvider.GetRequiredService<ShopContext>().Database.EnsureCreatedAsync();
		}

		for (var i = 0; i < 30; i++)
		{
			await using var scope = provider.CreateAsyncScope();
			var db = scope.ServiceProvider.GetRequiredService<ShopContext>();
			db.Orders.Add(new Order { Customer = "ada", Total = -1 });
			var act = async () => await db.SaveChangesAsync();
			await act.Should().ThrowAsync<ConstraintViolationException>();
		}
	}
}
