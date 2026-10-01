using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Finance.Ledger;

namespace Nullean.Entities.Examples.Tests.Finance;

public class LedgerTests
{
	private static readonly ConstraintSet<JournalEntry> Constraints = JournalEntryConstraints.Constraints;

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
		var channel = ConstraintChannel.For(posted).Enforce(Constraints);
		var result = channel.Apply(e => e with { Lines = [new("Cash", 200m, 0m), new("Rev", 0m, 200m)] });
		result.Should().BeOfType<ChannelResult<JournalEntry>.Failure>();
	}

	[Test]
	public void Channel_Noop_when_no_change_to_posted_entry()
	{
		var posted = Balanced() with { Status = EntryStatus.Posted };
		var channel = ConstraintChannel.For(posted).Enforce(Constraints);
		var result = channel.Apply(e => e);
		result.Should().BeOfType<ChannelResult<JournalEntry>.Noop>();
	}
}
