// What a user gets from the constraints they already declared: each CsCheck test style in one line.
// No assertions are written here — the constraints are the assertions.

using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Basics.Money;
using Nullean.Entities.Examples.Commerce.OrderLifecycle;
using Nullean.Entities.Examples.Finance.BankAccount;
using Nullean.Entities.Examples.Traffic.TrafficLight;

namespace Nullean.Entities.Examples.Tests.AutoTests;

public class AutoTestExamples
{
	private static readonly OrderLine A = new("A", 1, 10m);
	private static readonly OrderLine B = new("B", 2, 5m);

	// Order holds its lines in a list, which compares by reference, so say what makes two orders "the same state".
	private static EntitySpec<Order> OrderSpec() =>
		OrderConstraints.All
			.Spec(Order.New(Guid.Empty), OrderConstraints.Actions)
			.Domain(A, B)
			.Domain("A", "B")
			.Boundary(o => o.Lines.Count < 2)
			.Key(o => (o.Status, string.Join(",", o.Lines.Select(l => l.Sku).OrderBy(s => s, StringComparer.Ordinal))));

	[Test]
	public void Order_every_reachable_state_and_step_satisfies_every_constraint_and_every_rule_is_exercised()
	{
		var report = OrderSpec().Exhaustive();

		report.Closed.Should().BeTrue();
	}

	[Test]
	public void Order_AddLine_never_shrinks_the_total_while_the_general_set_still_holds() =>
		OrderSpec().StepScoped("AddLine", OrderStepConstraints.AddLineStep).Exhaustive();

	[Test]
	public void Order_diagram_of_the_lifecycle_can_be_generated_for_docs() => OrderSpec().Mermaid().Should().StartWith("flowchart");

	[Test]
	public void Order_adding_two_lines_in_either_order_gives_the_same_order() => OrderSpec().Commutes("AddLine", "AddLine");

	[Test]
	public void Order_cancel_is_idempotent_from_every_reachable_state() => OrderSpec().Idempotent("Cancel");

	[Test]
	public void Order_random_walks_for_a_space_too_large_to_close() => OrderSpec().Sample(iter: 200);

	[Test]
	public void TrafficLight_cycles_through_exactly_its_three_signals()
	{
		var light = new TrafficLight("Main/1st", Signal.Red, TimeSpan.FromSeconds(30));

		var report = TrafficLightConstraints.All.Spec(light, TrafficLightConstraints.Actions).Exhaustive();

		report.States.Should().Be(3);
	}

	[Test]
	public void BankAccount_exhaustive_search_finds_that_Withdraw_is_unguarded()
	{
		// A real finding in the example domain: Withdraw never checks the overdraft limit and DepositIsMonotonic
		// sits in the general set, so a single Withdraw breaks both. The counterexample is the shortest one.
		var account = new BankAccount("NL00TEST", 100m, 100m, AccountStatus.Active);
		var spec = BankAccountConstraints.All.Spec(account, BankAccountConstraints.Actions).Domain(50m, 500m).Boundary(a => a.Balance is > -1000m and < 1000m);

		var act = () => spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("Withdraw");
	}

	// ── Zero config: AutoBogus supplies the starting states and the argument values ──────────────────

	[Test]
	public void Order_zero_config_explores_from_generated_valid_orders() =>
		OrderConstraints.Instance.Spec().Exhaustive().States.Should().BeGreaterThan(3);

	[Test]
	public void TrafficLight_zero_config_proves_the_cycle() => TrafficLightConstraints.Instance.Spec().Exhaustive();

	[Test]
	public void BankAccount_zero_config_preserves_finds_an_unguarded_action()
	{
		// Hostile arguments find the more basic gap first: Deposit(-1) is accepted by the method. Withdraw ignoring the
		// overdraft limit is the same kind of finding. Either way the action relies on Apply to say no.
		var act = () => BankAccountConstraints.All.Preserves(BankAccountConstraints.Actions, iter: 500);

		act.Should().Throw<Exception>().Which.Message.Should().MatchRegex("Withdraw|Deposit");
	}

	// ── Simple property tests: no state machine, constraints are the oracle ─────────────────────────

	[Test]
	public void Order_total_always_equals_its_lines() =>
		OrderConstraints.All.ForAll(o => o.Total == o.Lines.Sum(l => l.Quantity * l.Price), GenAuto.Of<Order>(AutoMode.Plausible), iter: 200);

	[Test]
	public void Order_cancelling_twice_is_the_same_as_once_for_any_valid_order() =>
		OrderConstraints.All.ForAll(o => o.Cancel().Cancel() == o.Cancel(), GenAuto.Of<Order>(AutoMode.Plausible), iter: 200);

	[Test]
	// Plausible only: Order.Total multiplies quantity by price, which overflows decimal on extreme lines (see AUTOTEST_NOTES.md).
	public void Order_every_action_keeps_a_generated_valid_order_valid() =>
		OrderConstraints.All.Preserves(OrderConstraints.Actions, iter: 300, data: AutoMode.Plausible);

	[Test]
	public void TrafficLight_probe_shows_which_rules_a_generator_can_actually_break()
	{
		var report = TrafficLightConstraints.All.Probe(throwOnContradiction: false);

		report.Entries.Should().NotBeEmpty();
	}

	[Test]
	public void TrafficLight_values_that_break_a_rule_are_named_and_rejected() => TrafficLightConstraints.All.RejectsInvalid();

	// ── Boundaries: where do the rules start and stop saying no? ─────────────────────────────────────

	[Test]
	public void BankAccount_boundaries_list_the_hostile_values_nothing_rejects()
	{
		var report = BankAccountConstraints.All.Boundaries(writeLine: Console.WriteLine);

		report.Gaps.Should().Contain(g => g.Property == "Iban" && g.Value == "\" \""); // a blank IBAN is accepted
		report.Gaps.Should().Contain(g => g.Property == "Status"); // so is an undefined AccountStatus
		report.RulesFor("Balance").Should().Contain("balance-above-limit"); // while the overdraft rule does reject extremes
	}

	[Test]
	public void Money_default_generator_breaks_every_rule_with_no_hand_made_data() => MoneyConstraints.All.Probe().NeverViolated.Should().BeEmpty();
}
