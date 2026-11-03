using AwesomeAssertions;
using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;

namespace Nullean.Entities.ReactiveUI.Tests;

public sealed record Booking(DateTimeOffset At)
{
	public static readonly ConstraintSet<Booking> Rules = ConstraintSet.From(
		("InTheFuture", Constraints<Booking>.Always((b, now) => b.At > now, "must be in the future"))
	);
}

public sealed class BookingViewModel : ReactiveObject, IValidatableViewModel
{
	private DateTimeOffset _at;

	public DateTimeOffset At { get => _at; set => this.RaiseAndSetIfChanged(ref _at, value); }

	public IValidationContext ValidationContext { get; } = new ValidationContext();
}

public class TimeTests
{
	[Test]
	public void Time_rules_read_the_clock_from_the_options()
	{
		var clock = new ManualTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
		var vm = new BookingViewModel { At = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero) };
		using var rules = vm.ValidateConstraints(Booking.Rules, v => new Booking(v.At), new ConstraintValidationOptions { Time = clock });
		vm.ValidationContext.IsValid.Should().BeTrue();

		vm.At = new DateTimeOffset(2029, 12, 31, 0, 0, 0, TimeSpan.Zero);

		vm.ValidationContext.IsValid.Should().BeFalse();
		vm.ValidationContext.Text.ToSingleLine().Should().Be("must be in the future");
	}
}
