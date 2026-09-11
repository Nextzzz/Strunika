using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;
using Strunika.Mobile.Theme;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Home of the four tabs. The tab views stay alive (the tuner and the
/// live detector keep their state); switching just toggles visibility
/// with a short cross-fade. The floating <see cref="PillTabBar"/> replaces
/// the native Shell tab bar (design decision, strunika-ui §5).
/// </summary>
public partial class RootPage : ContentPage
{
    private readonly View[] _tabs;
    private readonly TunerViewModel _tuner;
    private int _current;
    private Window? _window;

    public RootPage(TunerViewModel tuner, LiveViewModel live, LibraryViewModel library, SettingsViewModel settings)
    {
        InitializeComponent();
        Tuner.BindingContext = tuner;
        Live.BindingContext = live;
        Library.BindingContext = library;
        Settings.BindingContext = settings;
        _ = library.LoadAsync();
        _tabs = new View[] { Tuner, Live, Library, Settings };
        _tuner = tuner;

        TabBar.Tabs.Add(new PillTab("fork", Loc.Get("Tab_Tuner")));
        TabBar.Tabs.Add(new PillTab("mic", Loc.Get("Tab_Live")));
        TabBar.Tabs.Add(new PillTab("songs", Loc.Get("Tab_Songs")));
        TabBar.Tabs.Add(new PillTab("sliders", Loc.Get("Tab_Settings")));
        TabBar.Refresh();
#if IOS
        // Content runs under the home indicator like the system's own floating
        // tab bar (iOS 26 keeps that bar 21 pt off the screen's edge); the status
        // bar inset stays. Every layout that reaches the screen's bottom pads
        // itself by the safe area unless told not to (MAUI 10, and UIKit hands
        // each view its own share of the inset), so the tab bar's holder and
        // each tab's root layout are told; the tabs' 104 pt bottom paddings
        // then clear the bar as before. The legacy Page.UseSafeArea is gone
        // from the XAML: it still pads the whole page and hid all of this.
        Root.SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.None);
        TabHolder.SafeAreaEdges = SafeAreaEdges.None;
        BottomShade.SafeAreaEdges = SafeAreaEdges.None;
        foreach (var tab in new View[] { Tuner, Live, Library, Settings })
            SafeArea.IgnoreBelow(tab);
        TabBar.Margin = new Thickness(14, 0, 14, SafeArea.FloatingBarMargin);
#endif

        ApplyShade();
        AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.Theme)) ApplyShade(); };
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += (_, _) => ApplyShade();

        // A hidden tab is in the tree but has never been measured or realised by
        // the platform; the first switch pays for that and feels slow. While the
        // tuner is on screen the others are brought up one at a time, invisible,
        // so by the time a tab is tapped there is nothing left to build.
        Loaded += (_, _) =>
        {
            WatchWindow();
            _ = WarmTabsAsync();
        };

        Loc.Instance.PropertyChanged += (_, _) =>
        {
            TabBar.Tabs[0].Label = Loc.Get("Tab_Tuner");
            TabBar.Tabs[1].Label = Loc.Get("Tab_Live");
            TabBar.Tabs[2].Label = Loc.Get("Tab_Songs");
            TabBar.Tabs[3].Label = Loc.Get("Tab_Settings");
            TabBar.Refresh();
        };
    }

    /// <summary>
    /// The tuner listens while it is on screen. The root page coming into view
    /// with the tuner tab current — after the launch or the welcome screen, or
    /// when a sheet over it closes — starts it; a start while it already
    /// listens does nothing. On a new install this is what asks for the
    /// microphone, right after the welcome screen (user request 2026-09-11).
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_current == 0) _ = _tuner.StartListeningAsync();
    }

    /// <summary>The app going to the background lets the microphone go; coming
    /// back to the tuner takes it again — and tries again, should access have
    /// just been given in Settings.</summary>
    private void WatchWindow()
    {
        if (Window is not { } window || ReferenceEquals(window, _window)) return;
        _window = window;
        window.Stopped += (_, _) => _tuner.StopListening();
        window.Resumed += (_, _) =>
        {
            if (_current == 0 && Navigation.NavigationStack.LastOrDefault() == this)
                _ = _tuner.StartListeningAsync();
        };
    }

    private readonly HashSet<int> _warmed = new();

    private async Task WarmTabsAsync()
    {
        // One per turn, after a breath, so the tuner's own first frames are not
        // competing with this.
        for (int i = 1; i < _tabs.Length; i++)
        {
            await Task.Delay(400);
            if (_current == i || _warmed.Contains(i)) continue;
            var view = _tabs[i];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            view.Opacity = 0;
            view.InputTransparent = true;
            view.IsVisible = true;
            await Task.Yield();                                   // let a layout pass happen
            await Task.Delay(60);
            view.InputTransparent = false;
            // A tap during those 60 ms made this the current tab: leave it be.
            // Hiding it here left the Songs tab blank until it was reopened.
            if (_current != i) { view.IsVisible = false; view.Opacity = 1; }
            _warmed.Add(i);
            Strunika.Core.Diagnostics.FileLog.Info($"tab warm-up {i}: {clock.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>Content dims and slips under the floating bar: a plain XAML
    /// gradient from the page background at alpha 0 to opaque, in the current
    /// theme. (A canvas gradient rendered its transparent half white on some
    /// Windows machines, so this stays XAML.)</summary>
    private void ApplyShade()
    {
        // The shade is the page background itself (anything darker reads as a
        // contrasting band, per user feedback); only the ramp is steep, so
        // content dissolves into the background just above the bar.
        var shade = Tokens.Current("Bg");
        BottomShade.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                // 100 pt tall: 12 pt above the bar (buttons sit 104 pt up), then the bar + gap.
                // Steep: content is already dimming ~10 pt above the bar and is fully
                // shaded by the time it reaches the capsule.
                new GradientStop(shade.WithAlpha(0f), 0f),
                new GradientStop(shade.WithAlpha(0.6f), 0.08f),
                new GradientStop(shade.WithAlpha(0.9f), 0.2f),
                new GradientStop(shade, 0.4f),
                new GradientStop(shade, 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }

    private async void OnTabSelected(object? sender, int index)
    {
        if (index == _current || index < 0 || index >= _tabs.Length) return;
        var from = _tabs[_current];
        var to = _tabs[index];
        var clock = _warmed.Contains(index) ? null : System.Diagnostics.Stopwatch.StartNew();
        // Leaving a tab that listens releases the microphone.
        if (_current == 0) (Tuner.BindingContext as TunerViewModel)?.StopListening();
        if (_current == 1) (Live.BindingContext as LiveViewModel)?.StopListening();
        _current = index;
        if (index == 0) _ = _tuner.StartListeningAsync();          // the tuner listens while it is shown

        to.Opacity = 0;
        to.IsVisible = true;
        await Task.WhenAll(from.FadeToAsync(0, 90, Easing.CubicIn), to.FadeToAsync(1, 140, Easing.CubicOut));
        from.IsVisible = false;
        from.Opacity = 1;
        if (clock != null)
        {
            // The first time a tab is shown it is also built; the number says whether
            // the warm-up did its job (see WarmTabsAsync).
            _warmed.Add(index);
            Strunika.Core.Diagnostics.FileLog.Info($"tab {index} first shown: {clock.ElapsedMilliseconds} ms (fade 230 ms of it)");
        }
    }
}
