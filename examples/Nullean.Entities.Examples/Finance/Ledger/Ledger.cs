namespace Nullean.Entities.Examples.Finance.Ledger;

public enum EntryStatus
{
	Draft,
	Posted
}

public sealed record JournalLine(string Account, decimal Debit, decimal Credit);

public sealed record JournalEntry(Guid Id, EntryStatus Status, IReadOnlyList<JournalLine> Lines)
{
	public decimal NetBalance => Lines.Sum(l => l.Debit - l.Credit);
}

public sealed record Ledger(IReadOnlyList<JournalEntry> Entries);

[Constraints]
public static partial class JournalEntryConstraints
{
	public static readonly Constraint<JournalEntry> MustBalance = Constraints<JournalEntry>.Always(
		e => e.NetBalance == 0m,
		"A journal entry must balance: total debits must equal total credits."
	);

	public static readonly Constraint<JournalEntry> AtLeastTwoLines = Constraints<JournalEntry>.Always(
		e => e.Lines.Count >= 2,
		"A journal entry must have at least two lines."
	);

	// Posted entries are immutable — lines cannot change once posted.
	public static readonly Constraint<JournalEntry> PostedEntriesAreImmutable = Constraints<JournalEntry>.Transitions(
		(prev, next) => prev.Status != EntryStatus.Posted || next.Lines.SequenceEqual(prev.Lines),
		"Posted journal entries cannot be modified."
	);
}
