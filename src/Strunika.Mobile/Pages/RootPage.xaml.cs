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
    private int _current;

    public RootPage(TunerViewModel tuner, LiveViewModel live, LibraryViewModel library, SettingsViewModel settings)
    {
        InitializeComponent();
        Tuner.BindingContext = tuner;
        Live.BindingContext = live;
        Library.BindingContext = library;
        Settings.BindingContext = settings;
        _ = library.LoadAsync();
        _tabs = new View[] { Tuner, Live, Library, Settings };

        TabBar.Tabs.Add(new PillTab("fork", Loc.Get("Tab_Tuner")));
        TabBar.Tabs.Add(new PillTab("mic", Loc.Get("Tab_Live")));
        TabBar.Tabs.Add(new PillTab("songs", Loc.Get("Tab_Songs")));
        TabBar.Tabs.Add(new PillTab("sliders", Loc.Get("Tab_Settings")));
        TabBar.Refresh();

        ApplyShade();
        AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.Theme)) ApplyShade(); };
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += (_, _) => ApplyShade();

        // A hidden tab is in the tree but has never been measured or realised by
        // the platform; the first switch pays for that and feels slow. While the
        // tuner is on screen the others are brought up one at a time, invisible,
        // so by the time a tab is tapped there is nothing left to build.
        Loaded += (_, _) => _ = WarmTabsAsync();

        Loc.Instance.PropertyChanged += (_, _) =>
        {
            TabBar.Tabs[0].Label = Loc.Get("Tab_Tuner");
            TabBar.Tabs[1].Label = Loc.Get("Tab_Live");
            TabBar.Tabs[2].Label = Loc.Get("Tab_Songs");
            TabBar.Tabs[3].Label = Loc.Get("Tab_Settings");
            TabBar.Refresh();
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
            view.IsVisible = false;
            view.InputTransparent = false;
            view.Opacity = 1;
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

        to.Opacity = 0;
        to.IsVisible = true;
        await Task.WhenAll(from.FadeTo(0, 90, Easing.CubicIn), to.FadeTo(1, 140, Easing.CubicOut));
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
