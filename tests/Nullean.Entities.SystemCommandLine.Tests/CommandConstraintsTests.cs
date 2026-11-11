using System.CommandLine;
using AwesomeAssertions;

namespace Nullean.Entities.SystemCommandLine.Tests;

public sealed partial record ServeOptions(string Host, int Port, int MaxRetries)
{
	[Constraint]
	public static readonly Constraint<ServeOptions> PortInRange = Constraints<ServeOptions>.Check(
		o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Port), "must be between 1 and 65535")]
	);

	[Constraint]
	public static readonly Constraint<ServeOptions> RetriesAreBounded = Constraints<ServeOptions>.Check(
		o => o.MaxRetries <= 10 ? [] : [Violation.Error(nameof(MaxRetries), "must be 10 or fewer")]
	);

	// A rule about the whole object: no member, so the message stands on its own.
	[Constraint]
	public static readonly Constraint<ServeOptions> PublicHostNeedsHighPort = Constraints<ServeOptions>.Check(
		o => o.Host == "localhost" || o.Port > 1023 ? [] : [Violation.Error("a public host needs a port above 1023")]
	);

	[Constraint]
	public static readonly Constraint<ServeOptions> RetriesOff = Constraints<ServeOptions>.Check(
		o => o.MaxRetries > 0 ? [] : [Violation.Warning("retries are off")]
	);
}

/// <summary>The program the README and the docs page show: the options, one bind for the action and the check, and the action.</summary>
internal sealed class ServeApp
{
	public int Ran { get; private set; }

	public ServeOptions? Last { get; private set; }

	public RootCommand Root { get; }

	public ServeApp()
	{
		var host = new Option<string>("--host") { DefaultValueFactory = _ => "localhost" };
		var port = new Option<int>("--port", "-p") { DefaultValueFactory = _ => 8080 };
		var retries = new Option<int>("--max-retries") { DefaultValueFactory = _ => 3 };
		var root = new RootCommand("Serves the site") { host, port, retries };

		ServeOptions Bind(ParseResult r) => new(r.GetValue(host)!, r.GetValue(port), r.GetValue(retries));

		root.SetAction(result =>
		{
			Last = Bind(result); // runs only when the constraints hold
			Ran++;
		});
		root.ValidateConstraints(Bind); // after SetAction; ServeOptions declares its rules, or pass a set: ValidateConstraints(Bind, ServeRules.All)
		Root = root;
	}

	public async Task<(int Code, string Out, string Err)> Run(params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var code = await Root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
		return (code, output.ToString(), error.ToString());
	}
}

public sealed record ProxyOptions(string Upstream);

internal static class ProxyRules
{
	public static readonly ConstraintSet<ProxyOptions> All = ConstraintSet.From(
		("UpstreamIsHttps", Constraints<ProxyOptions>.Check(
			o => o.Upstream.StartsWith("https://", StringComparison.Ordinal) ? [] : [Violation.Error(nameof(ProxyOptions.Upstream), "must be an https:// URL")]
		))
	);
}

public class CommandConstraintsTests
{
	private static async Task<(int Code, string Out, string Err)> Invoke(Command command, params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var code = await command.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
		return (code, output.ToString(), error.ToString());
	}

	[Test]
	public async Task A_violation_is_reported_like_a_parse_error_naming_the_option_and_the_action_does_not_run()
	{
		var app = new ServeApp();

		var (code, output, err) = await app.Run("--port", "70000");

		code.Should().Be(1);
		err.Should().Be("--port: must be between 1 and 65535" + Environment.NewLine + Environment.NewLine);
		output.Should().Contain("Usage:").And.Contain("--max-retries");
		app.Ran.Should().Be(0);
	}

	[Test]
	public async Task The_output_matches_a_parse_error_from_System_CommandLine_itself()
	{
		var app = new ServeApp();

		var (parseCode, parseOut, parseErr) = await app.Run("--port", "abc");
		var (code, output, _) = await app.Run("--port", "70000");

		code.Should().Be(parseCode);
		output.Should().Be(parseOut);
		parseErr.Should().Contain("Cannot parse argument 'abc' for option '--port'");
		app.Ran.Should().Be(0);
	}

	[Test]
	public async Task A_valid_command_runs()
	{
		var app = new ServeApp();

		var (code, _, err) = await app.Run("--port", "8081");

		code.Should().Be(0);
		err.Should().BeEmpty();
		app.Ran.Should().Be(1);
		app.Last.Should().Be(new ServeOptions("localhost", 8081, 3));
	}

	[Test]
	public async Task Option_defaults_are_applied_before_the_check()
	{
		var app = new ServeApp();

		// --port is omitted: its DefaultValueFactory gives 8080. A command validator would have seen 0 here.
		var (code, _, err) = await app.Run();

		code.Should().Be(0);
		err.Should().BeEmpty();
		app.Last.Should().Be(new ServeOptions("localhost", 8080, 3));
	}

	[Test]
	public async Task An_alias_on_the_command_line_still_names_the_option()
	{
		var app = new ServeApp();

		var (code, _, err) = await app.Run("-p", "0");

		code.Should().Be(1);
		err.Should().StartWith("--port: must be between 1 and 65535");
		app.Ran.Should().Be(0);
	}

	[Test]
	public async Task A_multi_word_member_maps_to_its_kebab_case_option()
	{
		var app = new ServeApp();

		var (code, _, err) = await app.Run("--max-retries", "11");

		code.Should().Be(1);
		err.Should().StartWith("--max-retries: must be 10 or fewer");
		app.Ran.Should().Be(0);
	}

	[Test]
	public async Task A_rule_without_a_member_is_reported_as_its_message()
	{
		var app = new ServeApp();

		var (code, _, err) = await app.Run("--host", "example.com", "--port", "80");

		code.Should().Be(1);
		err.Should().StartWith("a public host needs a port above 1023" + Environment.NewLine);
		app.Ran.Should().Be(0);
	}

	[Test]
	public async Task Every_error_is_reported_one_per_line()
	{
		var app = new ServeApp();

		var (_, _, err) = await app.Run("--host", "example.com", "--port", "0", "--max-retries", "50");

		err
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
			.Should()
			.Equal("--port: must be between 1 and 65535", "--max-retries: must be 10 or fewer", "a public host needs a port above 1023");
	}

	[Test]
	public async Task A_warning_is_written_to_the_error_writer_and_does_not_fail()
	{
		var app = new ServeApp();

		var (code, _, err) = await app.Run("--max-retries", "0");

		code.Should().Be(0);
		err.Should().Be("Warning: retries are off" + Environment.NewLine);
		app.Ran.Should().Be(1);
	}

	[Test]
	public async Task A_parse_error_stops_the_command_before_the_constraints_run()
	{
		var to = new Option<string>("--to") { Required = true };
		var root = new RootCommand { to };
		var bound = 0;
		root.SetAction(_ =>
		{ });
		root.ValidateConstraints(
			r =>
			{
				bound++;
				return new ProxyOptions(r.GetValue(to)!);
			},
			ProxyRules.All
		);

		var (code, _, err) = await Invoke(root);

		code.Should().Be(1);
		err.Should().Contain("Option '--to' is required.");
		bound.Should().Be(0);
	}

	[Test]
	public async Task An_async_action_is_guarded_too()
	{
		var to = new Option<string>("--to");
		var root = new RootCommand { to };
		var ran = false;
		root.SetAction(async (_, ct) =>
		{
			await Task.Yield();
			ran = true;
			return 7;
		});
		root.ValidateConstraints(r => new ProxyOptions(r.GetValue(to)!), ProxyRules.All);

		(await Invoke(root, "--to", "http://example.com")).Code.Should().Be(1);
		ran.Should().BeFalse();
		(await Invoke(root, "--to", "https://example.com")).Code.Should().Be(7);
		ran.Should().BeTrue();
	}

	[Test]
	public async Task The_members_map_overrides_the_naming_convention()
	{
		var to = new Option<string>("--to");
		var root = new RootCommand { to };
		var ran = false;
		root.SetAction(_ => ran = true);
		root.ValidateConstraints(
			r => new ProxyOptions(r.GetValue(to)!),
			ProxyRules.All,
			new Dictionary<string, Symbol> { [nameof(ProxyOptions.Upstream)] = to }
		);

		var (code, _, err) = await Invoke(root, "--to", "http://example.com");

		code.Should().Be(1);
		err.Should().StartWith("--to: must be an https:// URL");
		ran.Should().BeFalse();
	}

	[Test]
	public async Task An_unmapped_member_is_named_as_is()
	{
		var to = new Option<string>("--to");
		var root = new RootCommand { to };
		root.SetAction(_ =>
		{ });
		root.ValidateConstraints(r => new ProxyOptions(r.GetValue(to)!), ProxyRules.All);

		var (_, _, err) = await Invoke(root, "--to", "http://example.com");

		err.Should().StartWith("Upstream: must be an https:// URL");
	}

	[Test]
	public async Task A_member_maps_to_a_positional_argument_by_name()
	{
		var upstream = new Argument<string>("upstream");
		var root = new RootCommand { upstream };
		root.SetAction(_ =>
		{ });
		root.ValidateConstraints(r => new ProxyOptions(r.GetValue(upstream)!), ProxyRules.All);

		var (_, _, err) = await Invoke(root, "http://example.com");

		err.Should().StartWith("<upstream>: must be an https:// URL");
	}

	[Test]
	public async Task A_subcommand_names_a_recursive_option_of_its_parent()
	{
		var upstream = new Option<string>("--upstream") { Recursive = true };
		var proxy = new Command("proxy");
		var root = new RootCommand { upstream, proxy };
		proxy.SetAction(_ =>
		{ });
		proxy.ValidateConstraints(r => new ProxyOptions(r.GetValue(upstream)!), ProxyRules.All);

		var (code, _, err) = await Invoke(root, "proxy", "--upstream", "http://example.com");

		code.Should().Be(1);
		err.Should().StartWith("--upstream: must be an https:// URL");
	}

	[Test]
	public async Task Without_an_action_it_throws_instead_of_checking_nothing()
	{
		var root = new RootCommand();

		var act = () => root.ValidateConstraints<ServeOptions>(_ => new ServeOptions("localhost", 80, 1));

		act.Should().Throw<InvalidOperationException>().WithMessage("*Call SetAction before ValidateConstraints*");
		await Task.CompletedTask;
	}
}
