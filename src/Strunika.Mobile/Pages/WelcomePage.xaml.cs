using System.Globalization;
using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.Pages;

/// <summary>
/// First launch: one screen — language and theme pre-filled from the
/// system, then straight to the tuner. Microphone permission is asked
/// later, in context.
/// </summary>
public partial class WelcomePage : ContentPage
{
    private readonly IServiceProvider _services;

    public WelcomePage(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        ApplyGreeting();
        Loc.Instance.PropertyChanged += (_, _) => ApplyGreeting();
        Loaded += (_, _) => _ = GreetAsync();

        LanguagePicker.SetItems(new[]
        {
            new SegmentItem(Loc.Get("Lang_Uk"), "flag_ua"),
            new SegmentItem(Loc.Get("Lang_En"), "flag_gb"),
        });
        LanguagePicker.SelectedIndex = Loc.Instance.LanguageCode == "uk" ? 0 : 1;
        LanguagePicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Segmented.SelectedIndex))
                Loc.Instance.Culture = new CultureInfo(LanguagePicker.SelectedIndex == 0 ? "uk" : "en");
        };

        ThemePicker.SetItems(new[]
        {
            new SegmentItem(Loc.Get("Theme_System"), "auto"),
            new SegmentItem(Loc.Get("Theme_Dark"), "moon"),
            new SegmentItem(Loc.Get("Theme_Light"), "sun"),
        });
        ThemePicker.SelectedIndex = AppSettings.ThemeIndex;
        ThemePicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Segmented.SelectedIndex))
                AppSettings.ThemeIndex = ThemePicker.SelectedIndex;
        };

        Loc.Instance.PropertyChanged += (_, _) =>
            ThemePicker.SetLabels(Loc.Get("Theme_System"), Loc.Get("Theme_Dark"), Loc.Get("Theme_Light"));
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        AppSettings.WelcomeDone = true;
        var root = _services.GetRequiredService<RootPage>();
        // Slide the root in underneath and drop this page from the stack —
        // replacing Window.Page outright collapses the WinUI window.
        Navigation.InsertPageBefore(root, this);
        await Navigation.PopAsync(animated: true);
    }


    /// <summary>Ukrainian gets its own lettering; every other language the English one.</summary>
    private void ApplyGreeting()
    {
        // Brand Accent per theme, exactly like the wave it wipes into: Gold on dark, Copper on light.
        string name = Loc.Instance.Culture.TwoLetterISOLanguageName == "uk" ? "hello_uk" : "hello_en";
        Greeting.SetAppTheme(Image.SourceProperty, ImageSource.FromFile(name + "_light.png"), ImageSource.FromFile(name + ".png"));
    }

    private bool _greeted;

    /// <summary>Wipe length; the dev head can stretch it (STRUNIKA_WIPE_MS) to inspect the join.</summary>
    private static uint WipeMs
    {
        get
        {
#if WINDOWS
            if (uint.TryParse(Environment.GetEnvironmentVariable("STRUNIKA_WIPE_MS"), out var ms) && ms > 0) return ms;
#endif
            return 2000;
        }
    }

    /// <summary>Experimental (2026-08-27): the familiar wave is shown first,
    /// (after the page fades in from the dark background in 0.5 s), then a slow
    /// left-to-right wipe replaces it with the lettered greeting
    /// while the strum plays. Reduce Motion: the greeting only.</summary>
    private async Task GreetAsync()
    {
        if (_greeted) return;
        _greeted = true;
        if (Services.Motion.Reduced)
        {
            Root.Opacity = 1;
            WaveClip.IsVisible = false;
            return;
        }
        const double H = 200;
        var waveClip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(0, 0, 4000, H));
        var wordClip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(0, 0, 0, H));
        WaveClip.Clip = waveClip;
        GreetingClip.Clip = wordClip;
        // The whole screen appears out of the dark in 0.5 s, then the string starts writing.
        await Root.FadeTo(1, 500, Easing.CubicOut);
        double width = GreetingClip.Width > 0 ? GreetingClip.Width : 342;
        var done = new TaskCompletionSource();
        new Animation(v =>
            {
                waveClip.Rect = new Rect(v, 0, Math.Max(0, width - v) + 1, H);
                wordClip.Rect = new Rect(0, 0, v, H);
            }, 0, width)
            .Commit(this, "greeting", 16, WipeMs, Easing.SinInOut, (_, _) => done.TrySetResult());
        // Sound after the wipe has started, off the UI thread (see the players).
        _ = Task.Run(() => _services.GetService<Services.ISoundPlayer>()?.PlayAsync(Services.SoundAssets.Greeting, Services.SoundAssets.GreetingVolume));
        await done.Task;
        WaveClip.IsVisible = false;
        GreetingClip.Clip = null;
    }
}
