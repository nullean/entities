using Banking;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using static Apps.Narrator;

// Money moving between accounts, with the rules a bank cannot get wrong. Run it with `dotnet run`.

const string Ada = "DE89 3704 0044 0532 0130 00";
const string Grace = "GB82 WEST 1234 5698 7654 32";
const string Euro = "EUR";

await using var provider = new ServiceCollection().AddBanking().BuildServiceProvider();
var accounts = provider.GetRequiredService<IAccountRepository>();
var bank = provider.GetRequiredService<ITransferService>();

async Task Open(string iban, string currency, decimal balance, decimal overdraft) =>
	await accounts.SaveAsync(new Account(Iban.Create(iban).Unwrap(), currency, balance, overdraft));

async Task<decimal> Balance(string iban) => (await accounts.FindAsync(Iban.Create(iban).Unwrap()))!.Balance;

static string Why(Result<Transfer> result) => string.Join("; ", result.ViolationsOrEmpty.Select(v => v.Message));

await Open(Ada, Euro, 1_000m, overdraft: 200m);
await Open(Grace, Euro, 50m, overdraft: 0m);

Section("Bad input: every problem at once, before any account is touched");
var bad = await bank.TransferAsync("DE00 0000 0000 0000 0000 00", "not an iban", -5m, "eur");
foreach (var v in bad.ViolationsOrEmpty)
{
	Say(v.Message);
}
Expect(bad.ViolationsOrEmpty.Count >= 4, "a wrong checksum, a malformed IBAN, a negative amount and a lower-case currency are all reported together");

Section("A good transfer moves money and conserves it");
var ok = await bank.TransferAsync(Ada, Grace, 250.50m, Euro);
Expect(ok.IsOk, "250.50 EUR from Ada to Grace");
Say($"Ada {await Balance(Ada)}, Grace {await Balance(Grace)}");
Expect(await Balance(Ada) + await Balance(Grace) == 1_050m, "the two balances still add up to 1050");

Section("The account's own rule: the overdraft");
var tooMuch = await bank.TransferAsync(Ada, Grace, 2_000m, Euro);
Say($"2000 EUR from Ada -> {Why(tooMuch)}");
Expect(tooMuch.IsFail, "Ada has 749.50 and a 200 overdraft: 2000 is refused");
Expect(await Balance(Ada) == 749.50m, "a refused transfer changes nothing");

var withinOverdraft = await bank.TransferAsync(Ada, Grace, 900m, Euro);
Expect(withinOverdraft.IsOk && await Balance(Ada) == -150.50m, "900 EUR is allowed: it uses 150.50 of the overdraft");

Section("Rules that only exist across the two accounts");
var self = await bank.TransferAsync(Grace, Grace, 1m, Euro);
Say($"Grace to Grace -> {Why(self)}");
Expect(self.IsFail, "an account cannot transfer to itself");

var dollars = await bank.TransferAsync(Grace, Ada, 1m, "USD");
Say($"1 USD between euro accounts -> {Why(dollars)}");
Expect(dollars.IsFail, "the currencies must match");

var unknown = await bank.TransferAsync(Grace, "NL91 ABNA 0417 1643 00", 1m, Euro);
Say($"To an IBAN with no account -> {Why(unknown)}");
Expect(unknown.IsFail, "an account that does not exist is refused");

Section("Concurrent transfers still add up");
await Open("FR14 2004 1010 0505 0001 3M02 606", Euro, 5_000m, overdraft: 0m);
var before = (await accounts.AllAsync()).Sum(a => a.Balance);
var parties = new[] { Ada, Grace, "FR1420041010050500013M02606" };
var random = new Random(7);
var transfers = Enumerable
	.Range(0, 200)
	.Select(_ => bank.TransferAsync(parties[random.Next(3)], parties[random.Next(3)], random.Next(1, 400), Euro))
	.ToArray();
await Task.WhenAll(transfers);
var accepted = transfers.Count(t => t.Result.IsOk);
var after = (await accounts.AllAsync()).Sum(a => a.Balance);
Say($"{accepted} of 200 accepted; total before {before}, after {after}");
Expect(before == after, "the total is the same after 200 concurrent transfers");
Expect((await accounts.AllAsync()).All(a => a.Balance >= -a.OverdraftLimit), "no account went past its overdraft");

return Finish();
