using EventSourcedCart;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using static Apps.Narrator;

// A cart as a stream of events. Run it with `dotnet run`.

await using var provider = new ServiceCollection().AddEventSourcedCart().BuildServiceProvider();
var store = provider.GetRequiredService<IEventStore>();
var carts = provider.GetRequiredService<CartService>();

static string Describe(Cart cart) => cart.Lines.Count == 0 ? "(empty)" : string.Join(", ", cart.Lines.Select(l => $"{l.Qty} x {l.Sku}"));

var id = Guid.NewGuid();

Section("Commands become events; the cart is whatever the events add up to");
await carts.HandleAsync(id, new CartCommand.AddItem("apple", 2));
await carts.HandleAsync(id, new CartCommand.AddItem("pear", 1));
await carts.HandleAsync(id, new CartCommand.AddItem("apple", 1));
Say($"cart: {Describe(await carts.GetAsync(id))}");
Expect(Describe(await carts.GetAsync(id)) == "3 x apple, 1 x pear", "three adds fold into two lines");

Section("The decider refuses what it should, and says so by producing nothing");
var tooMany = await carts.HandleAsync(id, new CartCommand.AddItem("apple", 1));
var unknown = await carts.HandleAsync(id, new CartCommand.AddItem("durian", 1));
var (history, _) = await store.LoadAsync(id);
Say($"a fourth apple and an item nobody sells: {history.Count} events in the stream, still");
Expect(history.Count == 3, "refused commands append nothing");
Expect(tooMany.IsOk && unknown.IsOk, "a refusal is not an error: the cart is simply unchanged");

Section("Replay: the cart as it was at any point");
Say($"after 2 events: {Describe(CartLogic.Rehydrate(history.Take(2)))}");
Expect(Describe(CartLogic.Rehydrate(history.Take(2))) == "2 x apple, 1 x pear", "the stream is the history");

Section("Checkout is final");
await carts.HandleAsync(id, new CartCommand.Checkout());
Say($"checked out; the stream now has {(await store.LoadAsync(id)).Events.Count} events, the last one carrying the total");
Expect((await carts.GetAsync(id)).CheckedOut, "the cart is checked out");
var after = await carts.HandleAsync(id, new CartCommand.RemoveItem("apple"));
Expect(after.Unwrap().Lines.Count == 2, "nothing can be removed after checkout");

Section("A wrong decider is stopped by the rules before anything is appended");
var careless = new CartService(store, (cart, command) => command is CartCommand.AddItem(var sku, var qty) ? [new CartEvent.ItemAdded(sku, qty)] : []);
var other = Guid.NewGuid();
var violation = await careless.HandleAsync(other, new CartCommand.AddItem("fig", 9));
Say($"a decider that forgot the per-item limit -> {string.Join("; ", violation.ViolationsOrEmpty.Select(v => v.Message))}");
Expect(violation.IsFail, "nine figs are refused by the rules");
Expect((await store.LoadAsync(other)).Events.Count == 0, "and nothing reached the stream");

Section("Two writers: the second one is told to reload");
var shared = Guid.NewGuid();
var (_, version) = await store.LoadAsync(shared);
await store.AppendAsync(shared, version, [new CartEvent.ItemAdded("fig", 1)]);
var stale = await store.AppendAsync(shared, version, [new CartEvent.ItemAdded("fig", 1)]);
Expect(!stale, "an append against a stale version is refused");

return Finish();
