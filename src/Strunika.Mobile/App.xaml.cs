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
        // The launch page unpacks models and warms the recognizer, then
        // hands over to Welcome (first run) or the root tabs.
        var window = new Window(new NavigationPage(_services.GetRequiredService<LaunchPage>()));
#if WINDOWS
        // iPhone-shaped dev window, pinned to the top of the screen: Windows
        // cascades new windows downwards and a 900-pt window then hangs below
        // a 1200-px display, which looks like clipped layout but is not.
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double screenHeight = display.Height / Math.Max(1, display.Density);
        // A device-shaped window: STRUNIKA_WINDOW=375x667 (launch profiles) or the
        // preset chosen in Settings → About — the same design must hold from
        // iPhone SE to iPad Pro.
        var (w, h) = Services.DevWindow.Startup();
        window.X = 40;
        window.Y = 0;
        window.Width = w;
        window.Height = Math.Min(h, screenHeight - 48);
#endif
        Theme.Metrics.Instance.Attach(window);
        return window;
    }
}
