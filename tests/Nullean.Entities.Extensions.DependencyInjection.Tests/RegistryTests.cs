using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Nullean.Entities.Extensions.DependencyInjection.Tests;

internal sealed partial record Port(int Value)
{
	[Constraint]
	public static readonly Constraint<Port> InRange = Constraints<Port>.Check(
		p => p.Value is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Value), "must be between 1 and 65535")]
	);
}

public class RegistryTests
{
	[Test]
	public void AddConstraints_registers_the_type_for_type_erased_validation()
	{
		var registry = new ServiceCollection().AddConstraints<Port>().BuildServiceProvider().GetRequiredService<IConstraintRegistry>();

		registry.TryValidate(new Port(0), out var violations).Should().BeTrue();
		violations.Should().ContainSingle(v => v.Member == "Value");
		registry.TryValidate(new Port(80), out violations).Should().BeTrue();
		violations.Should().BeEmpty();
		registry.TryValidate("not registered", out _).Should().BeFalse();
	}

	[Test]
	public void Every_AddConstraints_call_lands_in_the_one_registry()
	{
		var registry = new ServiceCollection()
			.AddConstraints<Port>()
			.AddConstraints(ConstraintSet.From(("NotEmpty", Constraints<string>.Always(s => s.Length > 0, "empty"))))
			.BuildServiceProvider()
			.GetRequiredService<IConstraintRegistry>();

		registry.IsRegistered(typeof(Port)).Should().BeTrue();
		registry.IsRegistered(typeof(string)).Should().BeTrue();
	}

	[Test]
	public void Violations_group_errors_by_member_and_drop_warnings()
	{
		var errors = ViolationErrors.ToDictionary([Violation.Error("A", "x"), Violation.Error("A", "y"), Violation.Warning("w")]);

		errors.Should().ContainSingle().Which.Value.Should().Equal("x", "y");
	}
}

public class OptionsTests
{
	private sealed class PortOptions
	{
		public int Port { get; set; }
	}

	private static readonly ConstraintSet<PortOptions> PortRules = ConstraintSet.From(
		("Port", Constraints<PortOptions>.Check(o => o.Port > 0 ? [] : [Violation.Error(nameof(PortOptions.Port), "must be positive")]))
	);

	[Test]
	public void ValidateConstraints_fails_the_options_with_member_prefixed_messages()
	{
		var sp = new ServiceCollection().AddOptions<PortOptions>().Configure(o => o.Port = 0).ValidateConstraints(PortRules).Services.BuildServiceProvider();

		var act = () => sp.GetRequiredService<IOptions<PortOptions>>().Value;

		act.Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle("Port: must be positive");
	}

	[Test]
	public void ValidateConstraints_passes_valid_options()
	{
		var sp = new ServiceCollection().AddOptions<PortOptions>().Configure(o => o.Port = 8080).ValidateConstraints(PortRules).Services.BuildServiceProvider();

		sp.GetRequiredService<IOptions<PortOptions>>().Value.Port.Should().Be(8080);
	}
}
