using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal abstract record Msg
{
	public sealed record Placed(int Id) : Msg;

	public sealed record Confirmed(int Id) : Msg;

	public sealed record Shipped(int Id) : Msg;

	public sealed record Cancelled(int Id) : Msg;
}

public class TemporalMonitorTests
{
	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

	private static IMonitor<Msg> Run(TemporalRule<Msg> rule, params (double Seconds, Msg Event)[] events)
	{
		var monitor = rule.CreateMonitor();
		foreach (var (seconds, e) in events)
		{
			monitor.Observe(e, At(seconds));
		}
		return monitor;
	}

	// ── Structural ───────────────────────────────────────────────────────────

	[Test]
	public void Always_is_violated_at_the_time_of_the_event_that_breaks_it()
	{
		var rule = Temporal<Msg>.Always<Msg.Placed>(p => p.Id > 0);

		var monitor = Run(rule, (1, new Msg.Placed(1)), (5, new Msg.Shipped(0)), (9, new Msg.Placed(0)));

		monitor.Current.IsViolated.Should().BeTrue();
		monitor.Current.ViolationAt.Should().Be(At(9));
	}

	[Test]
	public void A_violation_is_sticky_and_later_events_do_not_change_it()
	{
		var rule = Temporal<Msg>.Always<Msg.Placed>(p => p.Id > 0);

		var monitor = Run(rule, (1, new Msg.Placed(0)), (2, new Msg.Placed(0)));

		monitor.Current.ViolationAt.Should().Be(At(1));
	}

	[Test]
	public void Never_looks_at_consecutive_events_among_the_two_types_and_ignores_the_rest()
	{
		var rule = Temporal<Msg>.Never<Msg.Cancelled, Msg.Shipped>((c, s) => c.Id == s.Id);

		// a Confirmed sits between them but is neither type, so Cancelled and Shipped are still consecutive
		var monitor = Run(rule, (1, new Msg.Cancelled(1)), (2, new Msg.Confirmed(1)), (3, new Msg.Shipped(1)));

		monitor.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Never_is_quiet_when_the_pair_does_not_match()
	{
		var rule = Temporal<Msg>.Never<Msg.Cancelled, Msg.Shipped>((c, s) => c.Id == s.Id);

		Run(rule, (1, new Msg.Cancelled(1)), (2, new Msg.Shipped(2))).Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void NeverTwice_catches_a_duplicate_key_even_when_not_adjacent()
	{
		var rule = Temporal<Msg>.NeverTwice<Msg.Placed>(p => p.Id);

		var monitor = Run(rule, (1, new Msg.Placed(1)), (2, new Msg.Placed(2)), (3, new Msg.Shipped(1)), (4, new Msg.Placed(1)));

		monitor.Current.ViolationAt.Should().Be(At(4));
	}

	[Test]
	public void Transitions_checks_raw_adjacent_pairs()
	{
		var rule = Temporal<Msg>.Transitions((a, b) => !(a is Msg.Placed && b is Msg.Shipped));

		Run(rule, (1, new Msg.Placed(1)), (2, new Msg.Confirmed(1)), (3, new Msg.Shipped(1))).Current.IsPending.Should().BeTrue();
		Run(rule, (1, new Msg.Placed(1)), (2, new Msg.Shipped(1))).Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Transitions_by_type_is_an_allow_list()
	{
		var rule = Temporal<Msg>.Transitions((typeof(Msg.Placed), typeof(Msg.Confirmed)), (typeof(Msg.Confirmed), typeof(Msg.Shipped)));

		Run(rule, (1, new Msg.Placed(1)), (2, new Msg.Confirmed(1)), (3, new Msg.Shipped(1))).Current.IsPending.Should().BeTrue();
		Run(rule, (1, new Msg.Placed(1)), (2, new Msg.Shipped(1))).Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void When_requires_the_requirement_only_for_events_that_meet_the_guard()
	{
		var rule = Temporal<Msg>.When(e => e is Msg.Shipped).Requires(e => e is Msg.Shipped { Id: > 0 });

		Run(rule, (1, new Msg.Placed(0)), (2, new Msg.Shipped(3))).Current.IsPending.Should().BeTrue();
		Run(rule, (1, new Msg.Shipped(0))).Current.IsViolated.Should().BeTrue();
	}

	// ── Bounded response ─────────────────────────────────────────────────────

	private static readonly TemporalRule<Msg> ConfirmedShipsInAMinute = Temporal<Msg>
		.After<Msg.Confirmed>()
		.Then<Msg.Shipped>((c, s) => c.Id == s.Id)
		.Within(TimeSpan.FromSeconds(60));

	[Test]
	public void After_Within_is_satisfied_by_a_matching_event_in_time()
	{
		var monitor = Run(ConfirmedShipsInAMinute, (0, new Msg.Confirmed(1)), (59, new Msg.Shipped(1)));
		monitor.Tick(At(1_000));

		monitor.Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void After_Within_is_violated_at_the_deadline_not_at_the_time_it_was_noticed()
	{
		var monitor = Run(ConfirmedShipsInAMinute, (10, new Msg.Confirmed(1)));

		monitor.NextDeadline.Should().Be(At(70));
		monitor.Tick(At(500));

		monitor.Current.ViolationAt.Should().Be(At(70));
	}

	[Test]
	public void An_event_exactly_at_the_deadline_is_too_late()
	{
		// the deadline is reached before the event at the same instant is considered
		var monitor = Run(ConfirmedShipsInAMinute, (0, new Msg.Confirmed(1)), (60, new Msg.Shipped(1)));

		monitor.Current.IsViolated.Should().BeTrue();
		monitor.Current.ViolationAt.Should().Be(At(60));
	}

	[Test]
	public void An_event_that_is_both_trigger_and_answer_does_not_answer_its_own_obligation()
	{
		var rule = Temporal<Msg>.After<Msg.Confirmed>().Then<Msg.Confirmed>().Within(TimeSpan.FromSeconds(10));

		var monitor = Run(rule, (0, new Msg.Confirmed(1)));
		monitor.Tick(At(100));

		monitor.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void A_later_trigger_answers_an_earlier_obligation_of_the_same_kind()
	{
		var rule = Temporal<Msg>.After<Msg.Confirmed>().Then<Msg.Confirmed>().Within(TimeSpan.FromSeconds(10));

		var monitor = Run(rule, (0, new Msg.Confirmed(1)), (5, new Msg.Confirmed(2)));
		monitor.Tick(At(12));

		monitor.Current.IsPending.Should().BeTrue(); // the first is answered; the second is still open until 15
		monitor.Tick(At(20));
		monitor.Current.ViolationAt.Should().Be(At(15));
	}

	[Test]
	public void The_earliest_expired_obligation_is_the_one_reported()
	{
		var monitor = Run(ConfirmedShipsInAMinute, (0, new Msg.Confirmed(1)), (20, new Msg.Confirmed(2)));
		monitor.Tick(At(1_000));

		monitor.Current.ViolationAt.Should().Be(At(60));
	}

	[Test]
	public void Then_with_two_cases_accepts_either()
	{
		var rule = Temporal<Msg>
			.After<Msg.Confirmed>()
			.Then<Msg.Shipped, Msg.Cancelled>((c, s) => c.Id == s.Id, (c, x) => c.Id == x.Id)
			.Within(TimeSpan.FromSeconds(60));

		var monitor = Run(rule, (0, new Msg.Confirmed(1)), (5, new Msg.Cancelled(1)));
		monitor.Tick(At(1_000));

		monitor.Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void UnlessInterruptedBy_lifts_the_obligation_without_a_violation()
	{
		var rule = Temporal<Msg>.After<Msg.Confirmed>().Then<Msg.Shipped>().Within(TimeSpan.FromSeconds(60)).UnlessInterruptedBy<Msg.Cancelled>();

		var monitor = Run(rule, (0, new Msg.Confirmed(1)), (10, new Msg.Cancelled(1)));
		monitor.Tick(At(1_000));

		monitor.Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void Required_makes_a_trigger_that_never_fires_a_violation_at_finish()
	{
		var rule = Temporal<Msg>.After<Msg.Confirmed>().Then<Msg.Shipped>().Within(TimeSpan.FromSeconds(60)).Required();

		var monitor = Run(rule, (0, new Msg.Placed(1)));

		monitor.Current.IsPending.Should().BeTrue();
		monitor.Finish(At(5)).IsViolated.Should().BeTrue();
	}

	[Test]
	public void AfterUntil_is_the_predicate_form_of_the_same_rule()
	{
		var rule = Temporal<Msg>.AfterUntil(e => e is Msg.Confirmed, (t, e) => e is Msg.Shipped, TimeSpan.FromSeconds(30));

		var monitor = Run(rule, (0, new Msg.Confirmed(1)));
		monitor.Tick(At(31));

		monitor.Current.ViolationAt.Should().Be(At(30));
	}

	// ── Liveness ─────────────────────────────────────────────────────────────

	private static readonly TemporalRule<Msg> EveryPlacedShips = Temporal<Msg>.Eventually<Msg.Shipped>(e =>
			e switch
			{
				Msg.Placed p => p.Id,
				Msg.Shipped s => s.Id,
				_ => -1,
			}, e => e is Msg.Placed);

	[Test]
	public void Eventually_is_undecided_until_the_stream_ends()
	{
		var monitor = Run(EveryPlacedShips, (0, new Msg.Placed(1)));

		monitor.Current.IsPending.Should().BeTrue();
		monitor.Finish(At(100)).IsViolated.Should().BeTrue();
	}

	[Test]
	public void Eventually_holds_when_every_instance_closed()
	{
		var monitor = Run(EveryPlacedShips, (0, new Msg.Placed(1)), (1, new Msg.Placed(2)), (2, new Msg.Shipped(2)), (3, new Msg.Shipped(1)));

		monitor.Finish(At(10)).IsHolds.Should().BeTrue();
	}

	// ── Offline checking ─────────────────────────────────────────────────────

	[Test]
	public void Trace_Check_reports_violations_with_their_time_and_what_is_still_pending()
	{
		var rules = TemporalRuleSet<Msg>.From(
			("ConfirmedShips", ConfirmedShipsInAMinute),
			("EveryPlacedShips", EveryPlacedShips),
			("NoDuplicatePlaced", Temporal<Msg>.NeverTwice<Msg.Placed>(p => p.Id))
		);
		var trace = new[]
		{
			new Timestamped<Msg>(At(0), new Msg.Placed(1)),
			new Timestamped<Msg>(At(5), new Msg.Confirmed(1)),
			new Timestamped<Msg>(At(200), new Msg.Placed(2)),
		};

		var report = Trace.Check(trace, rules);

		report.Passed.Should().BeFalse();
		report.Violations.Select(v => v.Name).Should().BeEquivalentTo("ConfirmedShips", "EveryPlacedShips");
		report.Violations.Single(v => v.Name == "ConfirmedShips").Verdict.ViolationAt.Should().Be(At(65));
		report.Pending.Select(p => p.Name).Should().Equal("NoDuplicatePlaced");
		report.ToString().Should().Contain("VIOLATED ConfirmedShips");
	}

	[Test]
	public void Trace_Check_of_a_clean_trace_passes()
	{
		var rules = TemporalRuleSet<Msg>.From(("EveryPlacedShips", EveryPlacedShips), ("ConfirmedShips", ConfirmedShipsInAMinute));
		var trace = new[]
		{
			new Timestamped<Msg>(At(0), new Msg.Placed(1)),
			new Timestamped<Msg>(At(1), new Msg.Confirmed(1)),
			new Timestamped<Msg>(At(30), new Msg.Shipped(1)),
		};

		Trace.Check(trace, rules).Passed.Should().BeTrue();
	}
}

public class ManualTimeProviderTests
{
	[Test]
	public void Advance_moves_the_clock_and_fires_due_timers_in_order_each_seeing_its_own_time()
	{
		var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		var seen = new List<(string Name, TimeSpan Elapsed)>();
		var start = time.GetUtcNow();
		using var late = time.CreateTimer(_ => seen.Add(("late", time.GetUtcNow() - start)), null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
		using var early = time.CreateTimer(_ => seen.Add(("early", time.GetUtcNow() - start)), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

		time.Advance(TimeSpan.FromSeconds(30));

		seen.Should().Equal(("early", TimeSpan.FromSeconds(5)), ("late", TimeSpan.FromSeconds(20)));
		(time.GetUtcNow() - start).Should().Be(TimeSpan.FromSeconds(30));
	}

	[Test]
	public void A_timer_not_yet_due_does_not_fire()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		using var timer = time.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

		time.Advance(TimeSpan.FromSeconds(9));

		fired.Should().Be(0);
	}

	[Test]
	public void A_periodic_timer_fires_each_period_and_a_disposed_one_stops()
	{
		var time = new ManualTimeProvider();
		var fired = 0;
		var timer = time.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

		time.Advance(TimeSpan.FromSeconds(3));
		timer.Dispose();
		time.Advance(TimeSpan.FromSeconds(3));

		fired.Should().Be(3);
	}

	[Test]
	public void A_timer_created_inside_a_callback_fires_in_the_same_advance_when_due()
	{
		var time = new ManualTimeProvider();
		var order = new List<string>();
		using var outer = time.CreateTimer(
			_ =>
			{
				order.Add("outer");
				time.CreateTimer(_ => order.Add("inner"), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
			},
			null,
			TimeSpan.FromSeconds(1),
			Timeout.InfiniteTimeSpan
		);

		time.Advance(TimeSpan.FromSeconds(10));

		order.Should().Equal("outer", "inner");
	}

	[Test]
	public void Time_only_moves_forward()
	{
		var act = () => new ManualTimeProvider().Advance(TimeSpan.FromSeconds(-1));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}
}

public class TemporalOnlineTests
{
	private static readonly TemporalRuleSet<Msg> Rules = TemporalRuleSet<Msg>.From(
		("ConfirmedShips", Temporal<Msg>.After<Msg.Confirmed>().Then<Msg.Shipped>((c, s) => c.Id == s.Id).Within(TimeSpan.FromSeconds(60))),
		("EveryPlacedShips", Temporal<Msg>.Eventually<Msg.Shipped>(e => e is Msg.Placed p ? p.Id : e is Msg.Shipped s ? s.Id : -1, e => e is Msg.Placed))
	);

	[Test]
	public void A_missed_deadline_fires_from_the_timer_with_no_event_at_all()
	{
		var time = new ManualTimeProvider();
		using var monitor = new TemporalMonitor<Msg>(Rules, time);
		var confirmedAt = time.GetUtcNow();
		monitor.Observe(new Msg.Confirmed(1));

		time.Advance(TimeSpan.FromSeconds(59));
		monitor.AnyViolated.Should().BeFalse();
		time.Advance(TimeSpan.FromSeconds(2));

		monitor.AnyViolated.Should().BeTrue();
		monitor.Current.Single(c => c.Name == "ConfirmedShips").Verdict.ViolationAt.Should().Be(confirmedAt.AddSeconds(60));
	}

	[Test]
	public void An_answer_in_time_cancels_the_timer()
	{
		var time = new ManualTimeProvider();
		using var monitor = new TemporalMonitor<Msg>(Rules, time);
		monitor.Observe(new Msg.Confirmed(1));
		time.Advance(TimeSpan.FromSeconds(30));
		monitor.Observe(new Msg.Shipped(1));

		time.Advance(TimeSpan.FromHours(1));

		monitor.Current.Single(c => c.Name == "ConfirmedShips").Verdict.IsPending.Should().BeTrue();
	}

	[Test]
	public async Task Changes_streams_each_verdict_change_and_ends_on_Complete()
	{
		var time = new ManualTimeProvider();
		using var monitor = new TemporalMonitor<Msg>(Rules, time);
		var changes = new List<VerdictChange>();
		var reader = Task.Run(async () =>
		{
			await foreach (var change in monitor.Changes())
			{
				changes.Add(change);
			}
		});

		monitor.Observe(new Msg.Placed(1));
		monitor.Observe(new Msg.Confirmed(1));
		time.Advance(TimeSpan.FromSeconds(61));
		monitor.Complete();
		await reader;

		changes.Select(c => (c.Rule, c.Verdict.IsViolated)).Should().Equal(("ConfirmedShips", true), ("EveryPlacedShips", true));
	}

	[Test]
	public async Task MonitorAsync_runs_a_source_on_the_providers_clock()
	{
		var time = new ManualTimeProvider();

		async IAsyncEnumerable<Msg> Source()
		{
			yield return new Msg.Placed(1);
			await Task.Yield();
			yield return new Msg.Confirmed(1);
			time.Advance(TimeSpan.FromSeconds(90));
			await Task.Yield();
		}

		var changes = new List<VerdictChange>();
		await foreach (var change in Source().MonitorAsync(Rules, time))
		{
			changes.Add(change);
		}

		changes.Should().Contain(c => c.Rule == "ConfirmedShips" && c.Verdict.IsViolated);
		changes.Should().Contain(c => c.Rule == "EveryPlacedShips" && c.Verdict.IsViolated);
	}

	[Test]
	public async Task A_faulting_source_surfaces_to_the_consumer()
	{
		async IAsyncEnumerable<Msg> Source()
		{
			yield return new Msg.Placed(1);
			await Task.Yield();
			throw new InvalidOperationException("source broke");
		}

		var act = async () =>
		{
			await foreach (var _ in Source().MonitorAsync(Rules, new ManualTimeProvider()))
			{
			}
		};

		(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("source broke");
	}
}

public class AsyncApplyTests
{
	private static readonly ConstraintSet<Item> Rules = ConstraintSet.From(("Positive", Constraints<Item>.Always(i => i.Value > 0, "must be positive")));

	[Test]
	public async Task ApplyAsync_commits_a_valid_awaited_mutation()
	{
		var result = await Rules.ApplyAsync(new Item(1), async (i, _) =>
		{
			await Task.Yield();
			return i with { Value = 5 };
		});

		result.Should().BeOfType<Result<Item>.Ok>().Which.Value.Value.Should().Be(5);
	}

	[Test]
	public async Task ApplyAsync_rejects_an_invalid_awaited_mutation_carrying_what_was_attempted()
	{
		var result = await Rules.ApplyAsync(new Item(1), (i, _) => ValueTask.FromResult(i with { Value = -3 }));

		var fail = result.Should().BeOfType<Result<Item>.Fail>().Subject;
		fail.Attempted.Value.Should().Be(-3);
		fail.Violations.Should().ContainSingle(v => v.ConstraintId == "Positive");
	}

	[Test]
	public async Task ApplyDetailedAsync_tells_a_noop_apart()
	{
		var outcome = await Rules.ApplyDetailedAsync(new Item(1), (i, _) => ValueTask.FromResult(i));

		outcome.IsNoop.Should().BeTrue();
	}

	[Test]
	public async Task The_mutation_receives_the_cancellation_token()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var act = async () =>
			await Rules.ApplyAsync(new Item(1), async (i, ct) =>
			{
				await Task.Delay(TimeSpan.FromSeconds(30), ct);
				return i;
			}, cancellationToken: cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[Test]
	public async Task ConstrainedValue_only_keeps_an_accepted_awaited_change()
	{
		var value = ConstrainedValue.For(new Item(1)).Enforce(Rules);

		await value.ApplyAsync((i, _) => ValueTask.FromResult(i with { Value = 7 }));
		await value.ApplyAsync((i, _) => ValueTask.FromResult(i with { Value = -1 }));

		value.Current.Value.Should().Be(7);
	}

	[Test]
	public async Task ApplyAllAsync_applies_a_stream_of_mutations_and_reports_each_outcome()
	{
		static async IAsyncEnumerable<Func<Item, Item>> Mutations()
		{
			yield return i => i with { Value = 2 };
			await Task.Yield();
			yield return i => i with { Value = -9 };
			yield return i => i with { Value = i.Value + 1 };
		}

		var value = ConstrainedValue.For(new Item(1)).Enforce(Rules);
		var outcomes = new List<string>();
		await foreach (var outcome in value.ApplyAllAsync(Mutations()))
		{
			outcomes.Add(outcome.GetType().Name);
		}

		outcomes.Should().Equal("Applied", "Failure", "Applied");
		value.Current.Value.Should().Be(3);
	}

	[Test]
	public async Task ValidateAsync_validates_a_stream_without_materialising_it()
	{
		static async IAsyncEnumerable<Item> Items()
		{
			yield return new Item(1);
			await Task.Yield();
			yield return new Item(-1);
		}

		var results = new List<(Item Value, int Violations)>();
		await foreach (var (value, violations) in Rules.ValidateAsync(Items()))
		{
			results.Add((value, violations.Count));
		}

		results.Select(r => r.Violations).Should().Equal(0, 1);
	}
}
