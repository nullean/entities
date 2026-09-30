# Nullean.Entities.Bogus

Routes [Bogus](https://github.com/bchavez/Bogus) and
[AutoBogus](https://github.com/Soenneker/Soenneker.Utils.AutoBogus) draws through
[CsCheck](https://github.com/AnthonyLloyd/CsCheck)'s `PCG`, so a fake-data generator gets CsCheck's
seed replay and size-based shrinking for free. Standalone — no dependency on the other three
packages in this repo.

## Why

Bogus and CsCheck each have their own random source. Left alone, a Bogus-backed `Gen<T>` can't
replay a specific seed or shrink toward a minimal failing case, because CsCheck never sees the draws
that produced the value. This package makes Bogus draw from CsCheck's `PCG` instead of its own
`Random`, so every `Faker<T>`/`AutoFaker` value becomes a real CsCheck generator: reproducible by
seed, and shrinkable by draw count and magnitude.

## Faker-backed generators

`GenFakerExtensions.Faker<T>` wraps a `Faker<T>` configuration in a `Gen<T>`:

```csharp
using global::Bogus;
using Nullean.Entities.Bogus;

Gen<Order> genOrder = GenFakerExtensions.Faker<Order>(faker => faker
    .CustomInstantiator(f => new Order(f.Random.Guid()))
    .RuleFor(o => o.Email, f => f.Internet.Email())
    .RuleFor(o => o.Lines, f => f.Make(f.Random.Int(0, 10), () => new OrderLine
    {
        Sku = f.Commerce.Ean13(),
        Quantity = f.Random.Int(1, 100),
        Price = f.Finance.Amount(),
    }))
);

genOrder.Sample(order => order.Total.Should().Be(order.Lines.Sum(l => l.Quantity * l.Price)));
```

A failing case shrinks toward the order with the fewest lines and the smallest values, because
fewer/smaller Bogus draws produce a smaller CsCheck `Size`.

## Auto-populated generators

`GenAuto.Of<T>` fills every public member automatically via `AutoFaker`, still routed through the
same `PCG` — useful for types where hand-writing every `RuleFor` isn't worth it:

```csharp
Gen<Customer> genCustomer = GenAuto.Of<Customer>();

genCustomer.Sample(c => c.Email.Should().NotBeNullOrWhiteSpace());
// Nested objects (Address, etc.) are populated too — no configuration needed.
```

Pass a `configure` delegate to `GenAuto.Of` to add overrides or skip rules on the underlying
`AutoFaker`, same as configuring `AutoFaker` directly.

## What's routed through the PCG, and why it matters

- One `Faker<T>`/`AutoFaker` per thread, created on first use and reused — Bogus takes a global lock
  on every draw, so this only avoids repeated setup, not the lock itself.
- `configure` must be pure: it runs once per thread, not once per sample. Don't capture mutable
  state you intend to change per call.
- Only draws that represent real domain data count toward `Size` — a flag or a value that's always
  the same regardless of the input doesn't shrink meaningfully, so it isn't recorded.

## Install

```sh
dotnet add package Nullean.Entities.Bogus
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck) — drives a
  property-test action sequence; can use these generators for its own step arguments.
- License: MIT
