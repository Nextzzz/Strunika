using Strunika.Mobile.Pages;
using Strunika.Mobile.Services;

namespace Strunika.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
#if WINDOWS
        // Dev head: STRUNIKA_RESET=1 wipes preferences so the first-launch flow can be re-tested.
        if (Environment.GetEnvironmentVariable("STRUNIKA_RESET") == "1")
            Preferences.Default.Clear();
#endif
        AppSettings.ApplyTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page first = AppSettings.WelcomeDone
            ? _services.GetRequiredService<RootPage>()
            : _services.GetRequiredService<WelcomePage>();
        var window = new Window(new NavigationPage(first));
#if WINDOWS
        // iPhone-shaped dev window, pinned to the top of the screen: Windows
        // cascades new windows downwards and a 900-pt window then hangs below
        // a 1200-px display, which looks like clipped layout but is not.
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double screenHeight = display.Height / Math.Max(1, display.Density);
        window.X = 40;
        window.Y = 0;
        window.Width = 430;
        window.Height = Math.Min(900, screenHeight - 48);
#endif
        return window;
    }
}
