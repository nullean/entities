using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Scheduling.RoomBooking;

namespace Nullean.Entities.Examples.Tests.Scheduling;

public class RoomBookingTests
{
	private static readonly ConstraintSet<TimeSlot> SlotConstraints = TimeSlotConstraints.All;
	private static readonly ConstraintSet<BookingCalendar> CalConstraints = BookingConstraints.All;

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
	public void ShouldSatisfy_calendars_of_non_overlapping_hour_slots_are_valid() =>
		Gen.Int[0, 20]
			.List[1, 10]
			.Select(
				starts => new BookingCalendar("Room", [.. starts.Distinct().OrderBy(x => x).Select(h => new Booking(Guid.NewGuid(), "Room", Slot(h, h + 1)))])
			)
			.ShouldSatisfy(CalConstraints);

	[Test]
	public void RejectsInvalid_inverted_slots_are_named_and_rejected() => SlotConstraints.RejectsInvalid();
}
