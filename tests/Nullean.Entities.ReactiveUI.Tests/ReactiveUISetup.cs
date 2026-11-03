using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace Nullean.Entities.ReactiveUI.Tests;

internal static class ReactiveUISetup
{
	// ReactiveUI 26 registers its services (the property observers a view binding needs) through its builder; an app does this once at startup.
	[ModuleInitializer]
	internal static void Initialize() => RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
}
