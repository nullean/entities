using FluentValidation;
using FvSeverity = FluentValidation.Severity;

namespace Nullean.Entities.FluentValidation.Tests;

public sealed record Address(string Street, string City);

public sealed record OrderLine(string Sku, int Quantity);

public sealed record Customer(string Name, string Email, int Age, Address? Address, IReadOnlyList<OrderLine> Lines);

public static class AddressRules
{
	public static readonly ConstraintSet<Address> All = ConstraintSet.From(
		("city-required", Constraints<Address>.Check(
			a => string.IsNullOrWhiteSpace(a.City) ? [Violation.Error(nameof(Address.City), "a city is required")] : []
		))
	);
}

public static class LineRules
{
	public static readonly ConstraintSet<OrderLine> All = ConstraintSet.From(
		("quantity-positive", Constraints<OrderLine>.Check(
			l => l.Quantity > 0 ? [] : [Violation.Error(nameof(OrderLine.Quantity), "must be at least 1") with { Evidence = l.Quantity }]
		))
	);
}

public static class CustomerRules
{
	public static readonly ConstraintSet<Customer> All = ConstraintSet.From(
		("email-has-at", Constraints<Customer>.Check(
			c => c.Email.Contains('@', StringComparison.Ordinal) ? [] : [Violation.Error(nameof(Customer.Email), "must contain @") with { Evidence = c.Email }]
		)),
		("adult", Constraints<Customer>.Check(c => c.Age >= 18 ? [] : [Violation.Warning($"{c.Name} is under 18") with { Member = nameof(Customer.Age) }])),
		("short-name", Constraints<Customer>.Check(c => c.Name.Length >= 3 ? [] : [Violation.Hint("a name this short is unusual")])),
		("city-known", Constraints<Customer>.Check(c => c.Address is { City: "" } ? [Violation.Error("Address.City", "the city is empty")] : []))
	);

	public static Customer Valid => new("Ada", "ada@example.com", 36, new Address("1 Main St", "London"), [new OrderLine("A-1", 2)]);
}

/// <summary>An existing FluentValidation validator, with a nested and a collection rule.</summary>
public sealed class CustomerValidator : AbstractValidator<Customer>
{
	public CustomerValidator()
	{
		_ = RuleFor(c => c.Name).NotEmpty();
		_ = RuleFor(c => c.Email).EmailAddress().WithSeverity(FvSeverity.Warning);
		_ = RuleFor(c => c.Age).InclusiveBetween(18, 120);
		_ = RuleFor(c => c.Address!.City).NotEmpty().When(c => c.Address is not null);
		_ = RuleForEach(c => c.Lines).ChildRules(line => line.RuleFor(l => l.Quantity).GreaterThan(0).WithSeverity(FvSeverity.Info));
	}
}
