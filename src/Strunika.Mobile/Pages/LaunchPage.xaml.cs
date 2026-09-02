using Strunika.Core.Diagnostics;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Pages;

/// <summary>
/// First page of every launch: the brand mark with a breathing caption while
/// the app does its real start-up work (unpacking the ONNX models on first
/// run, opening the library). On iPhone this covers the seconds after the
/// static splash; the Windows head holds it for at least three seconds so
/// the screen can be seen at all (user request 2026-08-26).
/// </summary>
public partial class LaunchPage : ContentPage
{
    private static readonly string[] Models = { "btc_self", "btc_large_voca", "btc_guitar2" };
    private readonly IServiceProvider _services;
    private bool _done;

#if WINDOWS
    private const int MinimumMs = 3000;
#else
    private const int MinimumMs = 500;
#endif

    public LaunchPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ = Services.RemoteFlags.RefreshAsync();                 // kill switches, in the background
        if (_done) return;
        _done = true;

        // The library is read while the models unpack: by the time the tabs
        // appear the first page of songs is already in memory.
        _ = _services.GetRequiredService<ViewModels.LibraryViewModel>().PreloadAsync();

        // Build the tabs here rather than at the end: parsing four views' XAML and
        // creating their objects is the bulk of the wait, and it needs no window —
        // only the platform's own measuring waits for the tree (RootPage warms that
        // up once it is shown). The launch screen is exactly where this belongs.
        var buildClock = System.Diagnostics.Stopwatch.StartNew();
        _ = _services.GetRequiredService<RootPage>();
        FileLog.Info($"launch: tabs built in {buildClock.ElapsedMilliseconds} ms");

        bool unpacking = Models.Any(m => !File.Exists(Path.Combine(FileSystem.CacheDirectory, "models", m + ".onnx")));
        if (unpacking) Caption.Text = Loc.Get("Launch_Models");
        _ = RevealAsync();

        var minimum = Task.Delay(MinimumMs);
        try
        {
            await Task.WhenAll(Models.Select(ModelStore.EnsureAsync));
        }
        catch (Exception ex)
        {
            FileLog.Error("launch: model unpack", ex);   // the tabs cope with a missing model
        }
        await minimum;

        bool welcome = !AppSettings.SkipWelcome;
        Page next = welcome
            ? _services.GetRequiredService<WelcomePage>()
            : _services.GetRequiredService<RootPage>();
        // Replacing Window.Page collapses the WinUI window (README); swap pages inside the stack instead.
        Navigation.InsertPageBefore(next, this);
        // Welcome fades itself in from the dark, so no platform slide there; the tabs keep the slide.
        await Navigation.PopAsync(animated: !welcome && !Motion.Reduced);
    }

    /// <summary>The mark is already on screen (it is the static launch image);
    /// the wordmark and the caption come up around it, then the caption breathes.</summary>
    private async Task RevealAsync()
    {
        if (Motion.Reduced) { Title.Opacity = 1; Caption.Opacity = 1; return; }
        await Task.WhenAll(Title.FadeTo(1, 400, Easing.CubicOut), Caption.FadeTo(1, 400, Easing.CubicOut));
        while (Navigation.NavigationStack.Contains(this))
        {
            await Caption.FadeTo(0.35, 900, Easing.SinInOut);
            await Caption.FadeTo(1.0, 900, Easing.SinInOut);
        }
    }
}
