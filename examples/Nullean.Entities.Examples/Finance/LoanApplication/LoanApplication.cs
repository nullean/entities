namespace Nullean.Entities.Examples.Finance.LoanApplication;

public enum LoanStatus
{
	Draft,
	Submitted,
	Approved,
	Rejected,
	Withdrawn
}

public sealed record LoanApplicant(string Name, int CreditScore);

public sealed record LoanDocument(string Kind, bool IsVerified);

public sealed record LoanApplication(Guid Id, LoanStatus Status, LoanApplicant Applicant, IReadOnlyList<LoanDocument> Documents, decimal RequestedAmount)
{
	public bool HasRequiredDocs => Documents.Any(d => d.Kind == "ID" && d.IsVerified) && Documents.Any(d => d.Kind == "Income" && d.IsVerified);
}

[Constraints]
public static partial class LoanApplicationConstraints
{
	public static readonly Constraint<LoanApplication> LegalStatusTransitions = Constraints<LoanApplication>
		.Transitions(a => a.Status)
		.Allow(LoanStatus.Draft, LoanStatus.Submitted)
		.Allow(LoanStatus.Submitted, LoanStatus.Approved)
		.Allow(LoanStatus.Submitted, LoanStatus.Rejected)
		.Allow(LoanStatus.Draft, LoanStatus.Withdrawn)
		.Allow(LoanStatus.Submitted, LoanStatus.Withdrawn);

	// Approval requires verified documents.
	public static readonly Constraint<LoanApplication> ApprovalRequiresDocuments = Constraints<LoanApplication>
		.When(a => a.Status == LoanStatus.Approved)
		.Requires(a => a.HasRequiredDocs, "Approval requires verified ID and Income documents.");

	// Approval requires a sufficient credit score.
	public static readonly Constraint<LoanApplication> ApprovalRequiresCreditScore = Constraints<LoanApplication>
		.When(a => a.Status == LoanStatus.Approved)
		.Requires(a => a.Applicant.CreditScore >= 650, "Approval requires a credit score of at least 650.");

	public static readonly Constraint<LoanApplication> PositiveAmount = Constraints<LoanApplication>.Always(
		a => a.RequestedAmount > 0,
		"Requested amount must be positive."
	);
}
