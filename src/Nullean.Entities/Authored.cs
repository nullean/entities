namespace Nullean.Entities;

/// <summary>
/// A configuration value as it was written next to what the loader made of it. A loader that trims, lower-cases, defaults or expands a value hands the model only
/// the result, so no rule can say "the file wrote ' INTERNAL '" or "the file wrote nothing and the default was used". Keep both: <see cref="Raw"/> is the text
/// exactly as authored (null when the key was absent), <see cref="Value"/> is what the build uses. Rules about the authored form read <see cref="Raw"/>; the build reads <see cref="Value"/>.
/// </summary>
public readonly record struct Authored<T>(string? Raw, T Value)
{
	/// <summary>True when the file wrote the key at all, as opposed to the loader supplying the value.</summary>
	public bool IsWritten => Raw is not null;

	/// <summary>True when the value the build uses differs from what was written (it was trimmed, case-folded, expanded or defaulted).</summary>
	public bool WasChanged => Raw is not null && !string.Equals(Raw, $"{Value}", StringComparison.Ordinal);

	/// <summary>The value the build uses, so a record that holds an <see cref="Authored{T}"/> can be read where a <typeparamref name="T"/> is wanted.</summary>
	public static implicit operator T(Authored<T> authored) => authored.Value;
}

public static class Authored
{
	/// <summary>A value the loader supplied because the file did not write one.</summary>
	public static Authored<T> Defaulted<T>(T value) => new(null, value);

	/// <summary>A string taken exactly as written.</summary>
	public static Authored<string> Written(string raw) => new(raw, raw);

	/// <summary>A value the loader derived from what was written (<paramref name="raw"/> may be null when the key was absent).</summary>
	public static Authored<T> Of<T>(string? raw, T value) => new(raw, value);
}
