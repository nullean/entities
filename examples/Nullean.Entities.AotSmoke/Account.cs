using Nullean.Entities;

namespace Nullean.Entities.AotSmoke;

public sealed record FileHolder(System.IO.Abstractions.IDirectoryInfo Root, System.IO.Abstractions.IFileInfo File);

[Constraints]
public sealed partial record Account(string Id, decimal Balance)
{
	public static readonly Constraint<Account> NotOverdrawn = Constraints<Account>.Always(a => a.Balance >= 0m, "Balance must not be negative");

	public Account Withdraw(decimal amount) => this with { Balance = Balance - amount };
}
