using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using OrderFulfilment;
using static Apps.Narrator;

// An order service, end to end: dependency injection, an always-valid Email, a lifecycle machine that runs, rules checked on every transition.
// Run it with `dotnet run`; it exits non-zero if anything stops behaving as narrated.

await using var provider = new ServiceCollection().AddOrderFulfilment().BuildServiceProvider();
var orders = provider.GetRequiredService<IOrderService>();
var published = provider.GetRequiredService<InMemoryEventPublisher>();

static string Why(Result<Order> result) => string.Join("; ", result.ViolationsOrEmpty.Select(v => v.Message));

Section("An order cannot be created with a bad email");
var bad = await orders.CreateAsync("not-an-email");
Say($"CreateAsync(\"not-an-email\") -> {Why(bad)}");
Expect(bad.IsFail, "a bad email is refused before any order exists");

var created = await orders.CreateAsync("ada@example.com");
var order = created.Unwrap();
Expect(order.State is OrderState.Draft, $"a good email gives a draft order ({order.Id})");

Section("A transition the lifecycle does not have is refused, with the reason");
var tooEarly = await orders.ConfirmAsync(order.Id);
Say($"Confirm on a draft -> {Why(tooEarly)}");
Expect(tooEarly.IsFail, "a draft cannot be confirmed");

var placed = await orders.PlaceAsync(order.Id);
Expect(placed.IsOk, "Place moves the draft to Placed(0)");

Section("A guard looks at the data in the state");
var empty = await orders.ConfirmAsync(order.Id);
Say($"Confirm with no lines -> {Why(empty)}");
Expect(empty.IsFail, "an order with no lines cannot be confirmed");

await orders.AddLineAsync(order.Id);
var confirmed = await orders.ConfirmAsync(order.Id);
Expect(confirmed.IsOk, "with a line, Confirm succeeds");

Section("A rule about the whole order is enforced on the transition");
var unpaid = await orders.ShipAsync(order.Id, "");
Say($"Ship without a payment reference -> {Why(unpaid)}");
Expect(unpaid.IsFail, "a shipped order needs a payment reference");

var shipped = await orders.ShipAsync(order.Id, "pay-1001");
Expect(shipped.IsOk && shipped.Unwrap().State is OrderState.Shipped, "with a payment reference, Ship succeeds");

Section("A finished order stays finished");
var late = await orders.CancelAsync(order.Id);
Say($"Cancel after shipping -> {Why(late)}");
Expect(late.IsFail, "a shipped order cannot be cancelled");

Section("What was published");
foreach (var evt in published.Published)
{
	Say($"{evt.Change.From.GetType().Name} --{evt.Change.Trigger}--> {evt.Change.To.GetType().Name}");
}
Expect(
	published.Published.Select(e => e.Change.Trigger).SequenceEqual(["Place", "AddLine", "Confirm", "Ship"]),
	"only the accepted transitions were published, in order"
);

Section("The lifecycle, as a diagram");
Say(OrderLifecycle.Machine.ToMermaid().ReplaceLineEndings("\n   "));

return Finish();
