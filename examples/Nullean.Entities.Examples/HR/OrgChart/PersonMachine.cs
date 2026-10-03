// A person's employment status declared as data (see PersonConstraints.LegalStatusTransitions for the hand-written list it mirrors).

namespace Nullean.Entities.Examples.HR.OrgChart;

public static class PersonMachine
{
	public static readonly StateMachine<EmploymentStatus, string> Machine = StateMachine
		.Define<EmploymentStatus, string>(EmploymentStatus.Active)
		.Permit("GoOnLeave", EmploymentStatus.Active, EmploymentStatus.OnLeave)
		.Permit("Return", EmploymentStatus.OnLeave, EmploymentStatus.Active)
		.Permit("Terminate", [EmploymentStatus.Active, EmploymentStatus.OnLeave], EmploymentStatus.Terminated)
		.Build();
}
