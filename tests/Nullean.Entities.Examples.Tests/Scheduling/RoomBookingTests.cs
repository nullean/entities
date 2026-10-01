using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Scheduling.RoomBooking;

namespace Nullean.Entities.Examples.Tests.Scheduling;

public class RoomBookingTests
{
	private static readonly ConstraintSet<TimeSlot> SlotConstraints = TimeSlotConstraints.Constraints;
	private static readonly ConstraintSet<BookingCalendar> CalConstraints = BookingConstraints.Constraints;

	private static TimeSlot Slot(int startHour, int endHour) =>
		new(new DateTimeOffset(2024, 6, 1, startHour, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 6, 1, endHour, 0, 0, TimeSpan.Zero));

	[Test]
	public void Validate_valid_slot()
	{
		SlotConstraints.Validate(Slot(9, 10)).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_slot_where_end_before_start()
	{
		SlotConstraints.Validate(Slot(10, 9)).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_calendar_with_no_overlaps_passes()
	{
		var cal = new BookingCalendar("Room A", [new(Guid.NewGuid(), "Room A", Slot(9, 10)), new(Guid.NewGuid(), "Room A", Slot(10, 11))]);
		CalConstraints.Validate(cal).Should().BeEmpty();
	}

	[Test]
	public void Validate_calendar_with_overlap_fails()
	{
		var cal = new BookingCalendar("Room A", [new(Guid.NewGuid(), "Room A", Slot(9, 11)), new(Guid.NewGuid(), "Room A", Slot(10, 12))]);
		CalConstraints.Validate(cal).Should().NotBeEmpty();
	}

	[Test]
	public void CsCheck_non_overlapping_slots_always_valid()
	{
		Gen
			.Int[0, 20]
			.List[1, 10]
			.Select(starts =>
			{
				// Build non-overlapping 1-hour slots starting at hour 'start'
				var sorted = starts.Distinct().OrderBy(x => x).ToList();
				return sorted.Select(h => Slot(h, h + 1)).ToList();
			})
			.Sample(slots =>
			{
				var bookings = slots.Select(s => new Booking(Guid.NewGuid(), "Room", s)).ToList();
				var cal = new BookingCalendar("Room", bookings);
				CalConstraints.Validate(cal).Should().BeEmpty();
			});
	}
}
