// Wolverine 6: middleware whose Before method runs ahead of every handler it is applied to, validating the message through the registry.
// Throwing stops the handler; InvokeAsync rethrows it to the caller (a queued message goes through Wolverine's error policies instead).

using Nullean.Entities.Extensions.DependencyInjection;
using Wolverine;

namespace Nullean.Entities.Recipes.Wolverine;

public static class ConstraintMiddleware
{
	public static void Before(Envelope envelope, IConstraintRegistry registry)
	{
		if (envelope.Message is { } message && registry.TryValidate(message, out var violations) && violations.Any(v => v.Severity == Severity.Error))
		{
			throw new ConstraintViolationException(violations);
		}
	}
}

public sealed partial record ShipParcel(string Postcode, decimal WeightKg)
{
	[Constraint]
	public static readonly Constraint<ShipParcel> Weight = Constraints<ShipParcel>.Check(
		p => p.WeightKg is > 0 and <= 30 ? [] : [Violation.Error(nameof(WeightKg), "must be more than 0 and at most 30 kg")]
	);
}

public static class ShipParcelHandler
{
	public static string Handle(ShipParcel command) => $"shipping {command.WeightKg} kg to {command.Postcode}";
}
