// One set of rules, read by everything that needs them.
var rules = OrderConstraints.All;

// Where an order is built: a Result, never an invalid Order.
Result<Order> placed = rules.Validated(order);

// An API: a broken rule answers as ProblemDetails, keyed by field.
services.AddConstraints(rules).AddConstraintProblemDetails();

// Options, checked at start-up the same way.
services.AddOptions<ShopOptions>().ValidateConstraints(ShopOptions.Rules);

// The tests: one line, every axis the rules declare.
public Task Orders_keep_their_rules() => AutoTest.For(OrderConstraints.Instance, Orders).RunAsync();

// The docs: every reachable state, as a diagram.
string diagram = OrderConstraints.Instance.Spec().Mermaid();
