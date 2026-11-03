using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// CsCheck 5 stopped appending the inner exception's message to <c>CsCheckException.Message</c>. This library's reports (the
/// marked axis block, "Rule broken", a Preserves or Script reason) live in that inner exception, so every Sample call goes through here
/// to keep the 4.x message: CsCheck's text, seed included, then the reason.
/// </summary>
internal static class SampleCall
{
	public static void Run(Action sample)
	{
		try
		{
			sample();
		}
		catch (CsCheckException e) when (Rewrap(e) is { } full)
		{
			throw full;
		}
	}

	public static T Run<T>(Func<T> sample)
	{
		try
		{
			return sample();
		}
		catch (CsCheckException e) when (Rewrap(e) is { } full)
		{
			throw full;
		}
	}

	public static async Task RunAsync(Func<Task> sample)
	{
		try
		{
			await sample().ConfigureAwait(false);
		}
		catch (CsCheckException e) when (Rewrap(e) is { } full)
		{
			throw full;
		}
	}

	// CsCheck 4 built its message as message + '\n' + inner.Message. Null when there is nothing to add: no inner exception, or its
	// message is already there.
	internal static CsCheckException? Rewrap(CsCheckException e) =>
		e.InnerException is { } inner && (inner.Message.Length == 0 || !e.Message.Contains(inner.Message, StringComparison.Ordinal))
			? new CsCheckException(e.Message + '\n' + inner.Message, inner)
			: null;
}
