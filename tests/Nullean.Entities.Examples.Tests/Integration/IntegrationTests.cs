using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Integration.Examples.AspNet;
using Nullean.Entities.Integration.Examples.Marten;

namespace Nullean.Entities.Examples.Tests.Integration;

internal sealed record SignUp(string Email, int Age);

public class IntegrationTests
{
	private static readonly ConstraintSet<SignUp> SignUpRules = ConstraintSet.From(
		("EmailHasAt", Constraints<SignUp>.Always(s => s.Email, e => e.Contains('@'), "Email must contain an @.")),
		("AdultsOnly", Constraints<SignUp>.Always(s => s.Age, age => age >= 18, "You must be 18 or older.")),
		("NotBoth", Constraints<SignUp>.Check(s => s.Email == "x@x" && s.Age == 99 ? [Violation.Error("An unlikely pair.")] : []))
	);

	// ── ASP.NET Core ──────────────────────────────────────────────────────────────────────────────

	[Test]
	public void Violations_become_a_validation_problem_keyed_by_member_and_by_rule_name_when_the_rule_is_not_about_a_member()
	{
		var errors = ViolationErrors.ToDictionary(SignUpRules.Validate(new SignUp("nobody", 12)));

		errors.Keys.Should().BeEquivalentTo(["Email", "Age"]);
		errors["Email"].Should().ContainSingle().Which.Should().Be("Email must contain an @.");
		ViolationErrors.ToDictionary(SignUpRules.Validate(new SignUp("x@x", 99))).Keys.Should().BeEquivalentTo(["NotBoth"]);
	}

	[Test]
	public async Task The_endpoint_filter_answers_400_before_the_handler_runs_and_lets_a_valid_request_through()
	{
		var filter = new ConstraintFilter<SignUp>(SignUpRules);
		var handlerRan = false;

		ValueTask<object?> Handler(EndpointFilterInvocationContext _)
		{
			handlerRan = true;
			return ValueTask.FromResult<object?>("created");
		}

		var rejected = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(new DefaultHttpContext(), new SignUp("nobody", 12)), Handler);

		rejected
			.Should()
			.BeOfType<ProblemHttpResult>()
			.Which
			.ProblemDetails
			.Should()
			.BeOfType<HttpValidationProblemDetails>()
			.Which
			.Errors
			.Keys
			.Should()
			.Contain("Email");
		handlerRan.Should().BeFalse();

		var accepted = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(new DefaultHttpContext(), new SignUp("a@b", 30)), Handler);

		accepted.Should().Be("created");
		handlerRan.Should().BeTrue();
	}

	[Test]
	public void The_exception_ApplyOrThrow_throws_becomes_the_same_problem()
	{
		var act = () => SignUpRules.ApplyOrThrow(new SignUp("a@b", 30), _ => new SignUp("nobody", 30));

		var problem = act.Should().Throw<ConstraintViolationException>().Which.ToProblem();

		problem
			.Should()
			.BeOfType<ProblemHttpResult>()
			.Which
			.ProblemDetails
			.Should()
			.BeOfType<HttpValidationProblemDetails>()
			.Which
			.Errors
			.Keys
			.Should()
			.Contain("Email");
	}

	// ── Marten-style event-sourced aggregate ──────────────────────────────────────────────────────────

	private static readonly Invoice Start = new(Guid.Empty, 100m, 0m, false);

	private static EventSourcedSpec<Invoice, InvoiceCommand, object> Aggregate(Func<Invoice, InvoiceCommand, IEnumerable<object>> decide) =>
		EntitySpec.EventSourced<Invoice, InvoiceCommand, object>(
			Start,
			decide,
			(invoice, e) => invoice.Evolve(e),
			[new InvoiceCommand.Pay(50m), new InvoiceCommand.Pay(100m), new InvoiceCommand.Void()],
			InvoiceConstraints.All
		);

	[Test]
	public void The_aggregates_own_Apply_methods_are_explored_for_every_command_sequence()
	{
		var report = Aggregate(InvoiceDecider.Decide).Spec.Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().Be(4);
	}

	[Test]
	public void A_decide_without_the_overpayment_guard_is_caught_by_the_aggregates_constraints()
	{
		var act = () => Aggregate(InvoiceDecider.DecideWithoutOverpaymentGuard).Spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("NotOverpaid");
	}

	private static readonly TemporalRuleSet<object> NoPaymentAfterVoid = TemporalRuleSet<object>.From(
		("NoPaymentAfterVoid", Temporal<object>.Never<InvoiceVoided, PaymentReceived>((_, _) => true))
	);

	[Test]
	public void A_temporal_rule_over_the_events_is_proved_for_the_correct_decide_and_refuted_for_one_that_pays_a_voided_invoice()
	{
		Aggregate(InvoiceDecider.Decide).Exhaustive(NoPaymentAfterVoid).Closed.Should().BeTrue();

		static IEnumerable<object> PaysAfterVoid(Invoice invoice, InvoiceCommand command) => command switch
		{
			InvoiceCommand.Pay pay when invoice.Paid + pay.Amount <= invoice.Total => [new PaymentReceived(pay.Amount)],
			InvoiceCommand.Void when !invoice.Voided && invoice.Paid == 0 => [new InvoiceVoided()],
			_ => [],
		};

		var act = () => Aggregate(PaysAfterVoid).Exhaustive(NoPaymentAfterVoid);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("NoPaymentAfterVoid");
	}
}
