// Example 2: a retry-with-backoff HTTP client.
//
// The spec covers three temporal properties:
//   1. Backoff doubles on each retry.
//   2. Every request eventually either succeeds or gives up (within 30 s virtual time).
//   3. After giving up, no further retries fire for the same request ID.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
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

internal sealed class FlakyServer(IScheduler scheduler)
{
	public IObservable<ClientEvent> Events { get; } = Observable.Empty<ClientEvent>(scheduler);

	public void Enqueue(Request request) => throw new NotImplementedException("SUT stub");
	public void FailNext() => throw new NotImplementedException("SUT stub");
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
		.Delays(Gen.Int[0, 2_000].Select(ms => TimeSpan.FromMilliseconds(ms)));

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
		.Delays(Gen.Int[0, 2_000].Select(ms => TimeSpan.FromMilliseconds(ms)));

	[Test]
	public void BackoffAndTerminationAreRespected() =>
		RetryScript.SampleWith(scheduler => new FlakyServer(scheduler), server => server.Events, FlakyClientSpec.All);
}
