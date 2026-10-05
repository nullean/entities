using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Nullean.Entities.Extensions.DependencyInjection;

public static class ConstraintServiceCollectionExtensions
{
	/// <summary>Registers <c>T.Constraints</c> for a type that declares its own constraints (<c>[Constraints]</c> on the record itself).</summary>
	public static IServiceCollection AddConstraints<T>(this IServiceCollection services) where T : IHaveConstraints<T> =>
		services.AddConstraints(T.Constraints);

	/// <summary>Registers a constraint set, for example a companion's <c>OrderRules.All</c>.</summary>
	public static IServiceCollection AddConstraints<T>(this IServiceCollection services, ConstraintSet<T> constraints)
	{
		services.TryAddSingleton(sp =>
		{
			var registry = new ConstraintRegistry(sp.GetService<TimeProvider>());
			foreach (var step in sp.GetServices<IConfigureConstraintRegistry>())
			{
				step.Configure(registry);
			}
			return registry;
		});
		services.TryAddSingleton<IConstraintRegistry>(sp => sp.GetRequiredService<ConstraintRegistry>());
		services.AddSingleton(constraints);
		// A configuration step per call, so every AddConstraints lands in the one registry when it is first resolved.
		services.AddSingleton<IConfigureConstraintRegistry>(new ConfigureConstraintRegistry<T>(constraints));
		return services;
	}

	/// <summary>Registers a generated companion (<c>OrderRules.TypeConstraints</c>) and makes it resolvable as <see cref="IConstraintsFor{T}"/>.</summary>
	public static IServiceCollection AddConstraints<T, TFor>(this IServiceCollection services, TFor companion) where TFor : IConstraintsFor<T>
	{
		services.AddSingleton<IConstraintsFor<T>>(companion);
		return services.AddConstraints(companion.All);
	}

	/// <summary>Fails the options at first use (or at startup with <c>ValidateOnStart()</c>) when <typeparamref name="T"/> breaks its own constraints.</summary>
	public static OptionsBuilder<T> ValidateConstraints<T>(this OptionsBuilder<T> builder) where T : class, IHaveConstraints<T> =>
		builder.ValidateConstraints(T.Constraints);

	public static OptionsBuilder<T> ValidateConstraints<T>(this OptionsBuilder<T> builder, ConstraintSet<T> constraints) where T : class
	{
		builder.Services.AddSingleton<IValidateOptions<T>>(new ConstraintOptionsValidator<T>(builder.Name, constraints));
		return builder;
	}
}

internal interface IConfigureConstraintRegistry
{
	void Configure(ConstraintRegistry registry);
}

internal sealed class ConfigureConstraintRegistry<T>(ConstraintSet<T> constraints) : IConfigureConstraintRegistry
{
	public void Configure(ConstraintRegistry registry) => registry.Add(constraints);
}

internal sealed class ConstraintOptionsValidator<T>(string? name, ConstraintSet<T> constraints) : IValidateOptions<T> where T : class
{
	public ValidateOptionsResult Validate(string? optionsName, T options)
	{
		if (name is not null && optionsName != name)
		{
			return ValidateOptionsResult.Skip;
		}
		var errors = constraints
			.Validate(options)
			.Where(v => v.Severity == Severity.Error)
			.Select(v => v.Member is null ? v.Message : $"{v.Member}: {v.Message}")
			.ToList();
		return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
	}
}
