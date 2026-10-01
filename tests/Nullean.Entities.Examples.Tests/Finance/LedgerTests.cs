using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Finance.Ledger;

namespace Nullean.Entities.Examples.Tests.Finance;

public class LedgerTests
{
	private static readonly ConstraintSet<JournalEntry> Constraints = JournalEntryConstraints.All;

	private static JournalEntry Balanced() => new(Guid.NewGuid(), EntryStatus.Draft, [new("Cash", 100m, 0m), new("Revenue", 0m, 100m)]);

	[Test]
	public void Validate_balanced_two_line_entry_passes()
	{
		Constraints.Validate(Balanced()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_unbalanced_entry()
	{
		var entry = new JournalEntry(Guid.NewGuid(), EntryStatus.Draft, [new("Cash", 100m, 0m), new("Rev", 0m, 90m)]);
		Constraints.Validate(entry).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_rejects_single_line_entry()
	{
		var entry = new JournalEntry(Guid.NewGuid(), EntryStatus.Draft, [new("Cash", 100m, 100m)]);
		Constraints.Validate(entry).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Failure_when_modifying_posted_entry()
	{
		var posted = Balanced() with { Status = EntryStatus.Posted };
		var channel = ConstrainedValue.For(posted).Enforce(Constraints);
		var result = channel.ApplyDetailed(e => e with { Lines = [new("Cash", 200m, 0m), new("Rev", 0m, 200m)] });
		result.Should().BeOfType<ApplyOutcome<JournalEntry>.Failure>();
	}

	[Test]
	public void Channel_Noop_when_no_change_to_posted_entry()
	{
		var posted = Balanced() with { Status = EntryStatus.Posted };
		var channel = ConstrainedValue.For(posted).Enforce(Constraints);
		var result = channel.ApplyDetailed(e => e);
		result.Should().BeOfType<ApplyOutcome<JournalEntry>.Noop>();
	}

	// AutoBogus cannot build a balanced entry, so there is no valid start: names are still checked in Validate.
	[Test]
	public void RejectsInvalid_unbalanced_and_short_entries_are_named() => Constraints.RejectsInvalid();
}
