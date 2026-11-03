using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text;

namespace Nullean.Entities.SystemCommandLine;

/// <summary>
/// Validates the options object a System.CommandLine command binds against a constraint set, before the command's action runs. An error violation is reported the
/// way System.CommandLine reports a parse error (<c>--port: must be between 1 and 65535</c> on stderr, then the help), the action does not run, and the exit code is 1.
/// </summary>
/// <remarks>
/// The check wraps the command's action rather than adding a <see cref="Command.Validators">command validator</see>: System.CommandLine 2.0 runs command validators
/// before it fills in option defaults (<c>DefaultValueFactory</c>), so a validator binding the options would see <c>0</c> for an omitted <c>--port</c> that defaults to
/// <c>8080</c>. At invocation the parse result is complete: defaults are applied, values are converted and System.CommandLine's own errors have already stopped the run.
/// </remarks>
public static class CommandConstraints
{
	/// <summary>
	/// Checks <c>T.Constraints</c> against the options <paramref name="bind"/> builds, before <paramref name="command"/>'s action runs. Call after <c>SetAction</c>.
	/// </summary>
	/// <param name="command">The command whose action is guarded. Its action must already be set.</param>
	/// <param name="bind">Builds the options object from the parse result, the same way the action does (<c>result.GetValue(portOption)</c>).</param>
	/// <param name="members">
	/// Overrides for which option or argument a <see cref="Violation.Member"/> names. Members not listed here are matched by convention: the kebab-case of the member
	/// (<c>MaxRetries</c> to <c>max-retries</c>) against the option names and aliases without their prefix, and against argument names.
	/// </param>
	/// <exception cref="InvalidOperationException">The command has no action yet.</exception>
	public static void ValidateConstraints<T>(
		this Command command,
		Func<ParseResult, T> bind,
		IReadOnlyDictionary<string, Symbol>? members = null
	) where T : IHaveConstraints<T> =>
		command.ValidateConstraints(bind, T.Constraints, members);

	/// <summary>
	/// Checks <paramref name="constraints"/> against the options <paramref name="bind"/> builds, before <paramref name="command"/>'s action runs. Call after
	/// <c>SetAction</c>. Errors stop the command; warnings and hints are written to the invocation's error writer and do not.
	/// </summary>
	/// <param name="command">The command whose action is guarded. Its action must already be set.</param>
	/// <param name="bind">Builds the options object from the parse result, the same way the action does (<c>result.GetValue(portOption)</c>).</param>
	/// <param name="constraints">The rules to check, for example a companion's <c>All</c>.</param>
	/// <param name="members">
	/// Overrides for which option or argument a <see cref="Violation.Member"/> names. Members not listed here are matched by convention: the kebab-case of the member
	/// (<c>MaxRetries</c> to <c>max-retries</c>) against the option names and aliases without their prefix, and against argument names.
	/// </param>
	/// <exception cref="InvalidOperationException">The command has no action yet.</exception>
	public static void ValidateConstraints<T>(
		this Command command,
		Func<ParseResult, T> bind,
		ConstraintSet<T> constraints,
		IReadOnlyDictionary<string, Symbol>? members = null
	)
	{
		ArgumentNullException.ThrowIfNull(command);
		ArgumentNullException.ThrowIfNull(bind);
		ArgumentNullException.ThrowIfNull(constraints);
		var check = new Check<T>(bind, constraints, members);
		command.Action = command.Action switch
		{
			SynchronousCommandLineAction inner => new SyncGuard(inner, check.Run),
			AsynchronousCommandLineAction inner => new AsyncGuard(inner, check.Run),
			null =>
				throw new InvalidOperationException(
					$"Command '{command.Name}' has no action. Call SetAction before ValidateConstraints: the constraints are checked right before the action runs."
				),
			var other => throw new InvalidOperationException($"Command '{command.Name}' has an action of an unsupported type: {other.GetType().Name}."),
		};
	}

	private sealed class Check<T>(Func<ParseResult, T> bind, ConstraintSet<T> constraints, IReadOnlyDictionary<string, Symbol>? members)
	{
		/// <summary>Writes the warnings, and returns the error lines (empty when the command may run).</summary>
		public List<string> Run(ParseResult result)
		{
			var errors = new List<string>();
			foreach (var violation in constraints.Validate(bind(result)))
			{
				var message = Describe(violation, violation.Member is { } member ? Find(result.CommandResult, member, members) : null);
				if (violation.Severity == Severity.Error)
				{
					errors.Add(message);
				}
				else
				{
					result.InvocationConfiguration.Error.WriteLine($"{violation.Severity}: {message}");
				}
			}
			return errors;
		}
	}

	private sealed class SyncGuard(SynchronousCommandLineAction inner, Func<ParseResult, List<string>> check) : SynchronousCommandLineAction
	{
		public override bool Terminating => inner.Terminating;

		public override bool ClearsParseErrors => inner.ClearsParseErrors;

		public override int Invoke(ParseResult parseResult)
		{
			var errors = check(parseResult);
			if (errors.Count == 0)
			{
				return inner.Invoke(parseResult);
			}
			WriteErrors(parseResult, errors);
			// System.CommandLine's HelpAction is synchronous. A help option given an asynchronous action is skipped here rather than blocked on; an asynchronous
			// command action (AsyncGuard) awaits it.
			if (FindHelp(parseResult) is SynchronousCommandLineAction help)
			{
				_ = help.Invoke(parseResult);
			}
			return 1;
		}
	}

	private sealed class AsyncGuard(AsynchronousCommandLineAction inner, Func<ParseResult, List<string>> check) : AsynchronousCommandLineAction
	{
		public override bool Terminating => inner.Terminating;

		public override bool ClearsParseErrors => inner.ClearsParseErrors;

		public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
		{
			var errors = check(parseResult);
			if (errors.Count == 0)
			{
				return await inner.InvokeAsync(parseResult, cancellationToken).ConfigureAwait(false);
			}
			WriteErrors(parseResult, errors);
			switch (FindHelp(parseResult))
			{
				case SynchronousCommandLineAction help:
					_ = help.Invoke(parseResult);
					break;
				case AsynchronousCommandLineAction help:
					_ = await help.InvokeAsync(parseResult, cancellationToken).ConfigureAwait(false);
					break;
			}
			return 1;
		}
	}

	// What System.CommandLine's ParseErrorAction does with parse errors: each on its own line on the error writer, in red on a terminal, then a blank line.
	private static void WriteErrors(ParseResult result, List<string> errors)
	{
		var color = ColorsAreSupported;
		if (color)
		{
			Console.ResetColor();
			Console.ForegroundColor = ConsoleColor.Red;
		}
		var writer = result.InvocationConfiguration.Error;
		foreach (var error in errors)
		{
			writer.WriteLine(error);
		}
		writer.WriteLine();
		if (color)
		{
			Console.ResetColor();
		}
	}

	private static bool ColorsAreSupported =>
		!OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS() && !Console.IsOutputRedirected;

	// The help option's action of the nearest command that has one, as ParseErrorAction finds it.
	private static CommandLineAction? FindHelp(ParseResult result)
	{
		for (var command = result.CommandResult; command is not null; command = command.Parent as CommandResult)
		{
			foreach (var option in command.Command.Options)
			{
				if (option is HelpOption help)
				{
					return help.Action;
				}
			}
		}
		return null;
	}

	private static string Describe(Violation violation, Symbol? symbol) => symbol switch
	{
		Option option => $"{option.Name}: {violation.Message}",
		Argument argument => $"<{argument.Name}>: {violation.Message}",
		_ when violation.Member is { } member => $"{member}: {violation.Message}",
		_ => violation.Message,
	};

	// The command's own options and arguments first, then recursive options of the commands above it.
	private static Symbol? Find(CommandResult result, string member, IReadOnlyDictionary<string, Symbol>? members)
	{
		if (members is not null && members.TryGetValue(member, out var mapped))
		{
			return mapped;
		}

		var name = KebabCase(member);
		var own = true;
		for (var current = result; current is not null; current = current.Parent as CommandResult, own = false)
		{
			foreach (var option in current.Command.Options)
			{
				if ((own || option.Recursive) && (Matches(option.Name, name) || option.Aliases.Any(alias => Matches(alias, name))))
				{
					return option;
				}
			}
			if (!own)
			{
				continue;
			}
			foreach (var argument in current.Command.Arguments)
			{
				if (Matches(argument.Name, name) || string.Equals(argument.Name, member, StringComparison.OrdinalIgnoreCase))
				{
					return argument;
				}
			}
		}
		return null;
	}

	private static bool Matches(string optionName, string kebab) => string.Equals(optionName.TrimStart('-', '/'), kebab, StringComparison.OrdinalIgnoreCase);

	/// <summary><c>Port</c> to <c>port</c>, <c>MaxRetries</c> to <c>max-retries</c>, <c>HTTPPort</c> to <c>http-port</c>, <c>Address.City</c> to <c>address-city</c>.</summary>
	private static string KebabCase(string member)
	{
		var sb = new StringBuilder(member.Length + 4);
		for (var i = 0; i < member.Length; i++)
		{
			var c = member[i];
			if (c is '.' or '_' or '-')
			{
				if (sb.Length > 0 && sb[^1] != '-')
				{
					_ = sb.Append('-');
				}
				continue;
			}
			if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != '-')
			{
				var prev = member[i - 1];
				var nextIsLower = i + 1 < member.Length && char.IsLower(member[i + 1]);
				if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextIsLower))
				{
					_ = sb.Append('-');
				}
			}
			_ = sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}
}
