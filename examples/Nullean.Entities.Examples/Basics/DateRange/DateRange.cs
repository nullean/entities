namespace Nullean.Entities.Examples.Basics.DateRange;

public sealed record DateRange(DateOnly Start, DateOnly End)
{
	public DateRange Extend(DateOnly newEnd) => this with { End = newEnd };
}

[Constraints]
public static partial class DateRangeConstraints
{
	public static readonly Constraint<DateRange> StartBeforeEnd = Constraints<DateRange>.Always(
		r => r.Start <= r.End,
		"Start date must not be after End date."
	);

	// Extending a range can only push the end date forward.
	public static readonly Constraint<DateRange> EndOnlyMovesForward = Constraints<DateRange>.Monotonic(
		r => r.End,
		MonotonicDirection.NonDecreasing,
		"The end date can only move forward."
	);
}
