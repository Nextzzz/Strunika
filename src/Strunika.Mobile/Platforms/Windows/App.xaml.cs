using Microsoft.UI.Xaml;
using Strunika.Core.Diagnostics;

namespace Strunika.Mobile.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
		// Unpackaged WinUI swallows managed exceptions into a 0xc000027b crash;
		// write them to the Strunika log so the dev head is debuggable.
		UnhandledException += (_, e) =>
			FileLog.Error("WinUI unhandled: " + e.Message, e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
