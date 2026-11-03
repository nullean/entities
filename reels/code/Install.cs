// dotnet add package Nullean.Entities            the rules: no dependencies, native AOT
// dotnet add package Nullean.Entities.CsCheck    the tests: nine axes from one line

// Start a new rule as a warning; it becomes an error on the date you choose.
public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>
	.Always(o => o.Total >= 0, "An order total is never negative.")
	.WithSeverityUntil(Severity.Warning,
		until: new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.Zero),
		after: Severity.Error);

// entities.nullean.net
