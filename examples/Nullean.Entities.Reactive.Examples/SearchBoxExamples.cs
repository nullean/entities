// Style 1: Fluent ReactiveConstraintSet — inline constraint declarations next to the script.
// Style 2: Constraints class — a static class with named constraint fields.
// Style 3: Constraints as event source — expose the run as an IObservable<EntityEvent>.
//
// Domain: a search box that debounces keystrokes before querying.
// The SUT receives an IScheduler so everything runs in virtual time.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AwesomeAssertions;
using CsCheck;
using Microsoft.Reactive.Testing;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.SearchEvent>;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain ───────────────────────────────────────────────────────────────────

internal abstract record SearchEvent
{
	// Stamped with the scheduler's current time when the SUT emits the event.
	public DateTimeOffset Timestamp { get; init; }

	/// <summary>
	/// Shared marker for "the box's input changed somehow" — SettlesToQuery's
	/// <c>UnlessInterruptedBy&lt;InputChanged&gt;</c> needs Typed AND Cleared to both discharge a
	/// pending obligation, but only Typed may ever *open* one (<c>After&lt;Typed&gt;()</c> checks the
	/// concrete type, not this base). Using the same type for "trigger" and "interrupt" would make
	/// Cleared re-open the very obligation it was meant to close — an infinite regress a run of
	/// Clear-then-nothing would eventually violate on a timeout that was never really due.
	/// </summary>
	public abstract record InputChanged : SearchEvent;

	public sealed record Typed(string Text) : InputChanged;

	public sealed record Cleared : InputChanged;

	public sealed record Queried(string Text) : SearchEvent;
}

/// <summary>The SUT. Takes an <see cref="IScheduler"/> so debouncing runs in virtual time.</summary>
internal sealed class SearchBox(IScheduler scheduler, TimeSpan debounce) : IDisposable
{
	private readonly Subject<SearchEvent> _events = new();
	private IDisposable? _pendingQuery;

	public IObservable<SearchEvent> Events => _events;

	public void Dispose()
	{
		_pendingQuery?.Dispose();
		_events.Dispose();
	}

	public void Type(string text)
	{
		_events.OnNext(new SearchEvent.Typed(text) { Timestamp = scheduler.Now });
		_pendingQuery?.Dispose();

		// Defensive: the scripts below only ever generate non-empty text for Type (Clear is the
		// dedicated way to empty the box), but a caller could still pass "" directly — never
		// scheduling a query for it keeps NeverEmpty true by construction either way.
		if (text.Length == 0)
		{
			_pendingQuery = null;
			return;
		}

		_pendingQuery = scheduler.Schedule(debounce, () => _events.OnNext(new SearchEvent.Queried(text) { Timestamp = scheduler.Now }));
	}

	public void Clear()
	{
		_pendingQuery?.Dispose();
		_pendingQuery = null;
		_events.OnNext(new SearchEvent.Cleared { Timestamp = scheduler.Now });
	}
}

// ── Style 2: Constraints class ────────────────────────────────────────────────

/// <summary>
/// A named specification for <see cref="SearchBox"/>.
/// Each static field is a constraint; the field name becomes the constraint's label.
/// The class is itself testable — attach to any observable and assert on individual
/// constraint verdicts without running a property test.
/// </summary>
internal static class SearchBoxSpec
{
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	public static readonly ReactiveConstraint<SearchEvent> NeverEmpty = Always<SearchEvent.Queried>(q => q.Text != "").Named(nameof(NeverEmpty));

	public static readonly ReactiveConstraint<SearchEvent> SettlesToQuery = After<SearchEvent.Typed>()
		.Then<SearchEvent.Queried>((typed, query) => query.Text == typed.Text)
		.Within(TimeSpan.FromMilliseconds(350))
		.UnlessInterruptedBy<SearchEvent.InputChanged>()
		.Named(nameof(SettlesToQuery));

	public static readonly ReactiveConstraint<SearchEvent> NoBurstQueries = Never<SearchEvent.Queried, SearchEvent.Queried>(
		(prev, next) => next.Timestamp - prev.Timestamp < Debounce
	).Named(nameof(NoBurstQueries));

	public static readonly ReactiveConstraintSet<SearchEvent> All = ReactiveConstraintSet.From(
		(nameof(NeverEmpty), NeverEmpty),
		(nameof(SettlesToQuery), SettlesToQuery),
		(nameof(NoBurstQueries), NoBurstQueries)
	);
}

// ── Style 1: Fluent ReactiveConstraintSet ────────────────────────────────────

public class SearchBoxFluentExamples
{
	private static readonly Script<SearchBox> SearchScript = Script
		.For<SearchBox>()
		.Step(Gen.String[1, 5], (box, text) => box.Type(text))
		.Step("clear", box => box.Clear())
		.Delays(Gen.Int[0, 600].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(1, 20);

	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	// Constraints declared inline alongside the script that drives the SUT.
	private static readonly ReactiveConstraintSet<SearchEvent> Spec = ReactiveConstraintSet.From(
		("NeverEmpty", Always<SearchEvent.Queried>(q => q.Text != "")),
		("SettlesToQuery", After<SearchEvent.Typed>()
			.Then<SearchEvent.Queried>((typed, query) => query.Text == typed.Text)
			.Within(TimeSpan.FromMilliseconds(350))
			.UnlessInterruptedBy<SearchEvent.InputChanged>()),
		("NoBurstQueries", Never<SearchEvent.Queried, SearchEvent.Queried>((prev, next) => next.Timestamp - prev.Timestamp < Debounce))
	);

	[Test]
	public void DebounceIsRespected() => SearchScript.SampleWith(scheduler => new SearchBox(scheduler, Debounce), box => box.Events, Spec);
}

// ── Style 2: Constraints class usage ──────────────────────────────────────────

public class SearchBoxSpecClassExamples
{
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	private static readonly Script<SearchBox> SearchScript = Script
		.For<SearchBox>()
		.Step(Gen.String[1, 5], (box, text) => box.Type(text))
		.Step("clear", box => box.Clear())
		.Delays(Gen.Int[0, 600].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(1, 20);

	/// <summary>Property test via the constraints class.</summary>
	[Test]
	public void DebounceIsRespected() => SearchScript.SampleWith(scheduler => new SearchBox(scheduler, Debounce), box => box.Events, SearchBoxSpec.All);

	/// <summary>
	/// Unit test of a single constraint — no script needed.
	/// This verifies the constraint itself, not the SUT.
	/// </summary>
	[Test]
	public Task SettlesToQueryConstraintHoldsAfterDebounce()
	{
		var scheduler = new TestScheduler();
		var box = new SearchBox(scheduler, Debounce);

		// Attach the full set; access individual constraints by key.
		var running = SearchBoxSpec.All.Attach(box.Events, scheduler);

		// Initially pending.
		running[SearchBoxSpec.SettlesToQuery].Current.Should().Be(Verdict.Pending);

		// After a type + the debounce window, the obligation After<Typed>().Then<Queried>() opened
		// should have settled to Holds — a real assertion against the real SearchBox above, not a
		// stub.
		box.Type("a");
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);

		running[SearchBoxSpec.SettlesToQuery].Current.Should().Be(Verdict.Holds);
		return Task.CompletedTask;
	}
}

// ── Style 3: constraints as event source ─────────────────────────────────────

public class SearchBoxEventSourceExamples
{
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	private static readonly Script<SearchBox> SearchScript = Script
		.For<SearchBox>()
		.Step(Gen.String[1, 5], (box, text) => box.Type(text))
		.Step("clear", box => box.Clear())
		.Delays(Gen.Int[0, 600].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(1, 20);

	[Test]
	public async Task RunPublishesEvents()
	{
		var smaller = new List<EntityEvent.SmallerFailure>();

		// RunWith exposes the whole run as an observable rather than throwing.
		var run = SearchScript.RunWith(scheduler => new SearchBox(scheduler, Debounce), box => box.Events, SearchBoxSpec.All, iter: 500);

		run.OfType<EntityEvent.SmallerFailure>().Subscribe(smaller.Add);

		// Await completion (passes if the SUT is correct, throws otherwise).
		var result = await run.OfType<EntityEvent.Completed>().FirstAsync();
		result.Passed.Should().BeTrue();
	}
}
