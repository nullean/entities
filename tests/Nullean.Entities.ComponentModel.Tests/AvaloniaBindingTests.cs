using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AwesomeAssertions;

namespace Nullean.Entities.ComponentModel.Tests;

public sealed class TestApp : Application
{
	public override void Initialize() => Styles.Add(new FluentTheme());

	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// A real Avalonia 12 binding, on the headless platform: Avalonia.Headless has no TUnit adapter, so the test drives HeadlessUnitTestSession itself and runs
// each test body on its dispatcher thread. No extra binding setup: Avalonia's built-in INotifyDataErrorInfo plugin (on by default) does the work.
[NotInParallel]
public class AvaloniaBindingTests
{
	private static HeadlessUnitTestSession? Session;

	[Before(Class)]
	public static void Start() => Session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

	[After(Class)]
	public static void Stop() => Session?.Dispose();

	// The session completes a dispatch on its own thread, and an await on it would continue there, inline: the next test then dispatches from the
	// session's thread and waits on itself. Continue on the thread pool instead.
	private static async Task OnUiThread(Action body)
	{
		var dispatched = Session!.Dispatch(body, CancellationToken.None);
		await dispatched.ContinueWith(t => t, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
	}

	private static TextBox BoundTextBox(object viewModel, string path, out Window window)
	{
		var textBox = new TextBox();
		textBox.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
		window = new Window { DataContext = viewModel, Content = textBox };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		return textBox;
	}

	private static string[] Errors(Control control) => [.. (DataValidationErrors.GetErrors(control) ?? []).Select(e => e.ToString() ?? "")];

	[Test]
	public async Task TypingIntoATextBoxShowsTheConstraintMessage() =>
		await OnUiThread(() =>
		{
			var vm = new SignupViewModel();
			var email = BoundTextBox(vm, nameof(SignupViewModel.Email), out var window);
			DataValidationErrors.GetHasErrors(email).Should().BeFalse();

			email.Text = "not-an-email";
			Dispatcher.UIThread.RunJobs();

			vm.Email.Should().Be("not-an-email");
			DataValidationErrors.GetHasErrors(email).Should().BeTrue();
			Errors(email).Should().Equal("Email needs an @");

			email.Text = "ada@example.com";
			Dispatcher.UIThread.RunJobs();
			DataValidationErrors.GetHasErrors(email).Should().BeFalse();
			window.Close();
		});

	[Test]
	public async Task AnotherPropertysChangeUpdatesTheTextBox() =>
		await OnUiThread(() =>
		{
			// Name's TextBox shows "Name is required" as soon as Age changes: ErrorsChanged for Name reaches a binding that was not edited.
			var vm = new ToolkitSignupViewModel();
			var name = BoundTextBox(vm, nameof(ToolkitSignupViewModel.Name), out var window);

			vm.Age = 40;
			Dispatcher.UIThread.RunJobs();

			Errors(name).Should().Equal("Name is required");
			window.Close();
		});
}
