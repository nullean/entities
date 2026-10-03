// Copyright Nullean and contributors
// Licensed under the MIT License.

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// Creates CsCheck generators for any type from its shape. <see cref="Of{T}(AutoMode, Action{AutoOptions})"/> builds
/// values itself (plausible and adversarial, lock-free). An AutoBogus-backed generator is in the separate
/// <c>Nullean.Entities.CsCheck.AutoBogus</c> package.
/// </summary>
public static class GenAuto
{
	/// <summary>
	/// Creates a <see cref="Gen{T}"/> that automatically populates all members of
	/// <typeparamref name="T"/>, using Bogus locale-specific data for well-known types.
	/// </summary>
	/// <typeparam name="T">The type to generate.</typeparam>
	/// <param name="mode">How hostile the data is; see <see cref="AutoMode"/>.</param>
	/// <param name="configure">Optional tweaks: edge percentage, depth, collection size, custom generators for a type, extra string edges.</param>
	public static Gen<T> Of<T>(AutoMode mode = AutoMode.Mixed, Action<AutoOptions>? configure = null)
	{
		var options = new AutoOptions { Mode = mode };
		configure?.Invoke(options);
		return new AutoBuilder(options).For<T>();
	}
}
