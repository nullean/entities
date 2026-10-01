namespace Nullean.Entities.Examples.Scheduling.RoomBooking;

public sealed record TimeSlot(DateTimeOffset Start, DateTimeOffset End)
{
	public bool Overlaps(TimeSlot other) => Start < other.End && End > other.Start;
}

public sealed record Booking(Guid Id, string Room, TimeSlot Slot);

public sealed record BookingCalendar(string Room, IReadOnlyList<Booking> Bookings);

// TimeSlot-level constraints.
[Constraints]
public static partial class TimeSlotConstraints
{
	public static readonly Constraint<TimeSlot> SlotIsValid = Constraints<TimeSlot>.Always(s => s.Start < s.End, "Booking start must be before end.");
}

// Calendar-level constraints.
[Constraints]
public static partial class BookingConstraints
{
	public static readonly Constraint<BookingCalendar> NoOverlappingBookings = Constraints<BookingCalendar>.Check(cal =>
	{
		List<TimeSlot> slots = [.. cal.Bookings.Select(b => b.Slot)];
		return [
			.. slots.SelectMany(
				(s, i) => slots.Skip(i + 1).Where(s.Overlaps).Select(_ => Violation.Error($"Room '{cal.Room}' has overlapping bookings."))
			).Take(1)
		];
	});
}
