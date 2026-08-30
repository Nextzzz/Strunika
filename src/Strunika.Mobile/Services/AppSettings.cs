namespace Strunika.Mobile.Services;

/// <summary>
/// User preferences behind the Settings tab, persisted with Preferences.
/// Static on purpose: read from anywhere, one <see cref="Changed"/> event
/// for views that mirror a setting (e.g. expert mode on the Live tab).
/// </summary>
public static class AppSettings
{
    public static event EventHandler<string>? Changed;

    private static void Raise(string key) => Changed?.Invoke(null, key);

    // ---- welcome -----------------------------------------------------

    /// <summary>Skip the welcome screen (default off — user decision 2026-08-28:
    /// the greeting shows on every launch until it is turned off, first launch
    /// included; there is no separate "seen it once" flag any more).</summary>
    public static bool SkipWelcome
    {
        get => Preferences.Default.Get("skip_welcome", false);
        set { Preferences.Default.Set("skip_welcome", value); Raise(nameof(SkipWelcome)); }
    }

    // ---- appearance --------------------------------------------------

    /// <summary>"system" | "dark" | "light".</summary>
    public static string Theme
    {
        get => Preferences.Default.Get("ui_theme", "system");
        set
        {
            Preferences.Default.Set("ui_theme", value);
            ApplyTheme();
            Raise(nameof(Theme));
        }
    }

    public static int ThemeIndex
    {
        get => Theme switch { "dark" => 1, "light" => 2, _ => 0 };
        set => Theme = value switch { 1 => "dark", 2 => "light", _ => "system" };
    }

    public static void ApplyTheme()
    {
        if (Application.Current == null) return;
        Application.Current.UserAppTheme = Theme switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => AppTheme.Unspecified,
        };
    }

    /// <summary>Metronome level on the song page, 0–1.</summary>
    public static double ClickVolume
    {
        get => Preferences.Default.Get("click_volume", 0.8);
        set { Preferences.Default.Set("click_volume", Math.Clamp(value, 0, 1)); Raise(nameof(ClickVolume)); }
    }

    /// <summary>Song-page playback volume, 0–1.</summary>
    public static double SongVolume
    {
        get => Preferences.Default.Get("song_volume", 1.0);
        set { Preferences.Default.Set("song_volume", Math.Clamp(value, 0, 1)); Raise(nameof(SongVolume)); }
    }

    public static bool LeftHanded
    {
        get => Preferences.Default.Get("left_handed", false);
        set { Preferences.Default.Set("left_handed", value); Raise(nameof(LeftHanded)); }
    }

    // ---- tuner -------------------------------------------------------

    public static double A4Reference
    {
        get => Preferences.Default.Get("a4_reference", 440.0);
        set { Preferences.Default.Set("a4_reference", value); Raise(nameof(A4Reference)); }
    }

    public static string DefaultTuning
    {
        get => Preferences.Default.Get("default_tuning", "standard");
        set { Preferences.Default.Set("default_tuning", value); Raise(nameof(DefaultTuning)); }
    }

    // ---- recognition -------------------------------------------------

    /// <summary>Triads instead of sevenths everywhere (song page, live). The song
    /// page offers it too, but there is one value for the whole app.</summary>
    public static bool SimpleChords
    {
        get => Preferences.Default.Get("simple_chords", true);
        set { Preferences.Default.Set("simple_chords", value); Raise(nameof(SimpleChords)); }
    }

    public static bool BeatSnap
    {
        get => Preferences.Default.Get("beat_snap", true);
        set { Preferences.Default.Set("beat_snap", value); Raise(nameof(BeatSnap)); }
    }

    /// <summary>Model used for song analysis ("btc_self" by default — the
    /// legally clean self-trained model; see README "Model strategy").</summary>
    public static string SongModel
    {
        get => Preferences.Default.Get("song_model", "btc_self");
        set { Preferences.Default.Set("song_model", value); Raise(nameof(SongModel)); }
    }

    /// <summary>Ways of adding a song pinned to the top of the Songs screen
    /// ("youtube", "file", "record"), comma-separated.</summary>
    public static string PinnedSources
    {
        get => Preferences.Default.Get("pinned_sources", "youtube,file");
        set { Preferences.Default.Set("pinned_sources", value); Raise(nameof(PinnedSources)); }
    }

    /// <summary>Library sort: "date" | "title" | "key".</summary>
    public static string LibrarySort
    {
        get => Preferences.Default.Get("library_sort", "date");
        set { Preferences.Default.Set("library_sort", value); Raise(nameof(LibrarySort)); }
    }

    public static bool Expert
    {
        get => Preferences.Default.Get("expert_mode", false);
        set { Preferences.Default.Set("expert_mode", value); Raise(nameof(Expert)); }
    }
}
