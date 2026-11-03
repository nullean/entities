---
navigation_title: Test frameworks
description: One test per axis from AutoTest in TUnit, xUnit, NUnit and MSTest, with no test-framework dependency in the library.
---

# Test frameworks (TUnit, xUnit, NUnit, MSTest)

`Nullean.Entities.CsCheck` depends on no test framework. An auto-test throws when an axis fails, which every framework reports as a failed test, so `RunAsync()` works
anywhere. To get **one test per axis**, so that a broken axis fails only its own test and the test explorer shows which law broke, hand the cases to your framework's data source:

| Framework | Data source | Test |
|---|---|---|
| TUnit | `[MethodDataSource]` over `AsDataSource()` | `Task Axis(AxisCase axis) => axis.RunAsync()` |
| xUnit v3 (and v2) | `[MemberData]` over `TheoryData<string>` built from `CaseNames` | `Task Axis(string axis) => Auto().Case(axis).RunAsync()` |
| NUnit | `[TestCaseSource]` over `CaseNames` | the same |
| MSTest | `[DynamicData]` over `CaseNames` | the same |

`AutoTest<T>` and `EventAutoTest<TSut, TEvent>` both have `AsDataSource()`, `CaseNames` and `Case(name)`. The cases include the [deeper techniques](../guides/deeper.md)
you add (`Exhaustive`, `Commutes(Confirm, Cancel)`, ...), named the way they are displayed.

## Why names, not cases

TUnit takes the `AxisCase` itself. xUnit, NUnit and MSTest get the case's **name** instead, a plain string, and the test looks the case up again with `Case(name)`.
xUnit serializes theory data at discovery, and an `AxisCase` holds a delegate, which it cannot serialize. xUnit v3 then lists the whole theory as **one** test: it still runs
every axis, but the test explorer shows a single entry, and you cannot run or re-run one axis by itself. A string serializes in every framework, so each lists one test per axis.
NUnit 5 and MSTest 4 would also list `AxisCase` rows one per axis (they display `ToString()`, the axis name), but strings work the same way in all three.

Build the auto-test in a static method (`Auto()` below), not a field you mutate: the data source and the test each call it, so both see the same cases.

## TUnit

```csharp
public static IEnumerable<Func<AxisCase>> Axes() => Auto().AsDataSource();

[Test]
[MethodDataSource(nameof(Axes))]
public Task EveryDeclaredAxisHolds(AxisCase axis) => axis.RunAsync();
```

The complete file is `tests/Nullean.Entities.Quickstart.Tests/QuickstartTests.cs`, shown in [Getting started](../getting-started/index.md).

## xUnit v3

<!-- file: tests/Nullean.Entities.CsCheck.Xunit.Tests/XunitAxesTests.cs -->
```csharp
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;
using Xunit;

namespace Nullean.Entities.TestFrameworks.Xunit;

// The one-liner docs/integration/test-frameworks.md shows: one theory row per axis the rules declare.
public class XunitAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static TheoryData<string> Axes() => [.. Auto().CaseNames];

	[Theory]
	[MemberData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
```

`TheoryData<string>` keeps the xUnit analyzers quiet: an `IEnumerable<object[]>` member works too, but raises xUnit1042. Each test is displayed as
`EveryDeclaredAxisHolds(axis: "Monotonicity")`.

xUnit v2 has `TheoryData<T>` and serializes strings the same way, so the same lines apply. This repository tests xUnit v3 only.

## NUnit

<!-- file: tests/Nullean.Entities.CsCheck.NUnit.Tests/NUnitAxesTests.cs -->
```csharp
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;
using NUnit.Framework;

namespace Nullean.Entities.TestFrameworks.NUnit;

// The one-liner docs/integration/test-frameworks.md shows: one test case per axis the rules declare.
public class NUnitAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static IEnumerable<string> Axes() => Auto().CaseNames;

	[TestCaseSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
```

Each test is displayed as `EveryDeclaredAxisHolds("Monotonicity")`.

## MSTest

<!-- file: tests/Nullean.Entities.CsCheck.MSTest.Tests/MSTestAxesTests.cs -->
```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.TestFrameworks.MSTest;

// The one-liner docs/integration/test-frameworks.md shows: one data row per axis the rules declare.
[TestClass]
public class MSTestAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static IEnumerable<string> Axes() => Auto().CaseNames;

	[TestMethod]
	[DynamicData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
```

Each test is displayed as `EveryDeclaredAxisHolds ("Monotonicity")`.

## How this is tested

Every snippet above is a file in a test project of its own that `./build.sh test` runs with `dotnet run` (each is a Microsoft.Testing.Platform executable: xUnit v3 with
`UseMicrosoftTestingPlatformRunner`, NUnit with `EnableNUnitRunner`, MSTest with `EnableMSTestRunner`). Each project also runs itself again as a child process
(`tests/Shared/TestFrameworks/FrameworkChecks.cs`) and checks two things:

- `--list-tests` lists exactly one test per axis, one for each name in `CaseNames`.
- With an operation added that resets the order's version, the run reports one failed test, `Monotonicity`, and every other axis passed.

| Framework | Version tested |
|---|---|
| xUnit v3 | `xunit.v3` 4.0.2 |
| NUnit | `NUnit` 5.0.0 with `NUnit3TestAdapter` 6.3.1 |
| MSTest | `MSTest.TestFramework` and `MSTest.TestAdapter` 4.5.2 |
