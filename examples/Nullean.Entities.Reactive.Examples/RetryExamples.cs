// Example 2: a retry-with-backoff HTTP client.
//
// The spec covers three temporal properties:
//   1. Backoff doubles on each retry.
//   2. Every request eventually either succeeds or gives up (within 30 s virtual time).
//   3. After giving up, no further retries fire for the same request ID.

using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.ClientEvent>;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain ───────────────────────────────────────────────────────────────────

internal sealed record Request(Guid Id, string Path);

internal abstract record ClientEvent
{
	public sealed record Sent(Request Request) : ClientEvent;

	public sealed record Retried(Guid RequestId, TimeSpan Delay, TimeSpan PreviousDelay) : ClientEvent;

	public sealed record Succeeded(Guid RequestId) : ClientEvent;

	public sealed record GaveUp(Guid RequestId) : ClientEvent;
}

/// <summary>
/// A single shared "the next attempt fails" flag — a deliberately crude flakiness knob, not a
/// per-request one. Whichever request happens to attempt next consumes it; that's fine, since
/// every constraint below is scoped by RequestId and doesn't care which request the flag landed on.
/// </summary>
internal sealed class FlakyServer(IScheduler scheduler) : IDisposable
{
	private const int MaxAttempts = 4;
	private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(50);

	private readonly Subject<ClientEvent> _events = new();
	private bool _failNext;

	public IObservable<ClientEvent> Events => _events;

	public void Dispose() => _events.Dispose();

	public void FailNext() => _failNext = true;

	public void Enqueue(Request request)
	{
		_events.OnNext(new ClientEvent.Sent(request));
		Attempt(request, attempt: 0, delay: InitialDelay);
	}

	/// <summary>
	/// Tries the request now. On failure, schedules the next try after <paramref name="delay"/>
	/// and reports it as Retried once it fires, with the delay that just doubled — so
	/// <c>Delay == PreviousDelay * 2</c> holds for every Retried event, not just most of them.
	/// </summary>
	private void Attempt(Request request, int attempt, TimeSpan delay)
	{
		if (!_failNext)
		{
			_events.OnNext(new ClientEvent.Succeeded(request.Id));
			return;
		}
		_failNext = false;

		if (attempt >= MaxAttempts)
		{
			_events.OnNext(new ClientEvent.GaveUp(request.Id));
			return;
		}

		var nextDelay = delay + delay;
		scheduler.Schedule(delay, () =>
		{
			_events.OnNext(new ClientEvent.Retried(request.Id, nextDelay, delay));
			Attempt(request, attempt + 1, nextDelay);
		});
	}
}

// ── Constraints class ──────────────────────────────────────────────────────────

internal static class FlakyClientSpec
{
	public static readonly ReactiveConstraint<ClientEvent> BackoffDoubles = Always<ClientEvent.Retried>(r => r.Delay == r.PreviousDelay * 2).Named(
		nameof(BackoffDoubles)
	);

	public static readonly ReactiveConstraint<ClientEvent> EveryRequestEnds = After<ClientEvent.Sent>()
		.Then<ClientEvent.Succeeded, ClientEvent.GaveUp>(
			m1: (sent, done) => done.RequestId == sent.Request.Id,
			m2: (sent, done) => done.RequestId == sent.Request.Id
		)
		.Within(TimeSpan.FromSeconds(30));

	public static readonly ReactiveConstraint<ClientEvent> NoRetryAfterGiveUp = Never<ClientEvent.GaveUp, ClientEvent.Retried>(
		(gaveUp, retried) => retried.RequestId == gaveUp.RequestId
	).Named(nameof(NoRetryAfterGiveUp));

	public static readonly ReactiveConstraintSet<ClientEvent> All = ReactiveConstraintSet.From(
		(nameof(BackoffDoubles), BackoffDoubles),
		(nameof(EveryRequestEnds), EveryRequestEnds),
		(nameof(NoRetryAfterGiveUp), NoRetryAfterGiveUp)
	);
}

// ── Fluent style ─────────────────────────────────────────────────────────────

public class RetryFluentExamples
{
	private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

	private static readonly Script<FlakyServer> RetryScript = Script
		.For<FlakyServer>()
		.Step(Gen.String[1, 40].Select(path => new Request(Guid.NewGuid(), path)), (server, req) => server.Enqueue(req))
		.Step("fail-next", server => server.FailNext())
		.Delays(Gen.Int[0, 2_000].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	private static readonly ReactiveConstraintSet<ClientEvent> Spec = ReactiveConstraintSet.From(
		("BackoffDoubles", Always<ClientEvent.Retried>(r => r.Delay == r.PreviousDelay * 2)),
		("EveryRequestEnds", After<ClientEvent.Sent>()
			.Then<ClientEvent.Succeeded, ClientEvent.GaveUp>(
				m1: (sent, done) => done.RequestId == sent.Request.Id,
				m2: (sent, done) => done.RequestId == sent.Request.Id
			)
			.Within(MaxWait)),
		("NoRetryAfterGiveUp", Never<ClientEvent.GaveUp, ClientEvent.Retried>((g, r) => r.RequestId == g.RequestId))
	);

	[Test]
	public void BackoffAndTerminationAreRespected() => RetryScript.SampleWith(scheduler => new FlakyServer(scheduler), server => server.Events, Spec);
}

// ── Constraints class style ───────────────────────────────────────────────────

public class RetrySpecClassExamples
{
	private static readonly Script<FlakyServer> RetryScript = Script
		.For<FlakyServer>()
		.Step(Gen.String[1, 40].Select(path => new Request(Guid.NewGuid(), path)), (server, req) => server.Enqueue(req))
		.Step("fail-next", server => server.FailNext())
		.Delays(Gen.Int[0, 2_000].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	[Test]
	public void BackoffAndTerminationAreRespected() =>
		RetryScript.SampleWith(scheduler => new FlakyServer(scheduler), server => server.Events, FlakyClientSpec.All);
}
