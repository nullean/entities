using Microsoft.EntityFrameworkCore;

namespace Nullean.Entities.EntityFrameworkCore.Tests;

public enum OrderStatus
{
	Draft,
	Placed,
	Shipped,
	Cancelled,
}

// An EF entity as EF wants it: a mutable class with a parameterless constructor. The [Constraint] fields make it IHaveConstraints<Order>.
public sealed partial class Order
{
	public int Id { get; set; }
	public string Customer { get; set; } = "";
	public decimal Total { get; set; }
	public OrderStatus Status { get; set; }
	public int Version { get; set; }

	[Constraint]
	public static readonly Constraint<Order> TotalNotNegative = Constraints<Order>.Check(
		o => o.Total >= 0 ? [] : [Violation.Error(nameof(Total), "must not be negative")]
	);

	[Constraint]
	public static readonly Constraint<Order> HasCustomer = Constraints<Order>.Check(
		o => o.Customer.Length > 0 ? [] : [Violation.Error(nameof(Customer), "is required")]
	);

	[Constraint]
	public static readonly Constraint<Order> StatusFlow = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Draft, OrderStatus.Placed)
		.Allow(OrderStatus.Placed, OrderStatus.Shipped)
		.Allow(OrderStatus.Draft, OrderStatus.Cancelled)
		.Allow(OrderStatus.Placed, OrderStatus.Cancelled);

	[Constraint]
	public static readonly Constraint<Order> VersionOnlyGrows = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);

	[Constraint]
	public static readonly Constraint<Order> LargeOrderWarning = Constraints<Order>.Check(
		o => o.Total > 10_000 ? [Violation.Warning("Total is unusually large") with { Member = nameof(Total) }] : []
	);
}

// A type with no rules registered: the interceptor leaves it alone.
public sealed class Note
{
	public int Id { get; set; }
	public string Text { get; set; } = "";
}

public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
	public DbSet<Order> Orders => Set<Order>();
	public DbSet<Note> Notes => Set<Note>();
}
