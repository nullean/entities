using AwesomeAssertions;
using Nullean.Entities.Examples.Finance.LoanApplication;

namespace Nullean.Entities.Examples.Tests.Finance;

public class LoanApplicationTests
{
	private static readonly ConstraintSet<LoanApplication> Constraints = LoanApplicationConstraints.Constraints;

	private static LoanApplicant Applicant(int creditScore = 700) => new("John Smith", creditScore);

	private static LoanDocument Doc(string kind) => new(kind, IsVerified: true);

	private static LoanApplication Draft() => new(Guid.NewGuid(), LoanStatus.Draft, Applicant(), [], 10_000m);

	[Test]
	public void Validate_draft_application_passes()
	{
		Constraints.Validate(Draft()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_negative_amount()
	{
		var bad = Draft() with { RequestedAmount = -500m };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_submit()
	{
		var channel = ConstraintChannel.For(Draft()).Enforce(Constraints);
		var result = channel.Apply(a => a with { Status = LoanStatus.Submitted });
		result.Should().BeOfType<ChannelResult<LoanApplication>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_approve_without_documents()
	{
		var submitted = Draft() with { Status = LoanStatus.Submitted };
		var channel = ConstraintChannel.For(submitted).Enforce(Constraints);
		var result = channel.Apply(a => a with { Status = LoanStatus.Approved });
		result.Should().BeOfType<ChannelResult<LoanApplication>.Failure>();
	}

	[Test]
	public void Channel_Failure_on_illegal_transition_Draft_to_Approved()
	{
		var channel = ConstraintChannel.For(Draft()).Enforce(Constraints);
		var result = channel.Apply(a => a with { Status = LoanStatus.Approved });
		result.Should().BeOfType<ChannelResult<LoanApplication>.Failure>();
	}

	[Test]
	public void Channel_Applied_on_approve_with_valid_docs_and_credit()
	{
		var submitted = Draft() with { Status = LoanStatus.Submitted, Documents = [Doc("ID"), Doc("Income")] };
		var channel = ConstraintChannel.For(submitted).Enforce(Constraints);
		var result = channel.Apply(a => a with { Status = LoanStatus.Approved });
		result.Should().BeOfType<ChannelResult<LoanApplication>.Applied>();
	}
}
