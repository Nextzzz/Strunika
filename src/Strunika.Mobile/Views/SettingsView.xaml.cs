using Strunika.Mobile.Theme;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class SettingsView : ContentView
{
    public SettingsView()
    {
        InitializeComponent();
        // The rows fit themselves when they are resized; a language change or
        // a new size class changes the words and the sizes without moving the
        // rows (Theme.Refit runs the fit again after either).
        Theme.Refit.Watch(this, RefitRows);
    }

    private void RefitRows()
    {
        _proWaveWidth = -1;                                      // measure afresh, whatever it was
        OnTitleRowSized(null, EventArgs.Empty);
        OnProRowSized(null, EventArgs.Empty);
    }

    private double _proWaveWidth = -1;

    /// <summary>Below this the wave is a stub, not a string.</summary>
    private const double ProWaveMin = 26;

    /// <summary>
    /// The wave after "Pro" is decoration: it gets whatever the brand name and
    /// the button leave, up to 120 pt, and disappears below <see cref="ProWaveMin"/>
    /// rather than showing a clipped stub — on a 13 mini that is the difference
    /// between a short string and nothing at all. The button is never squeezed.
    /// </summary>
    private void OnProRowSized(object? sender, EventArgs e)
    {
        double row = ProRow.Width;
        if (row <= 0) return;
        double title = ((IView)ProTitle).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        double button = ((IView)ProButton).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        // A narrower right gap when space is tight: the string may come closer to
        // the button rather than vanish.
        // The wave's box carries the glow's bleed on both sides; its margins are
        // pulled in by as much, so the room the string itself takes is the box
        // less twice the bleed. The old 8 pt gap before it and 12 pt after it hold.
        const double bleed = Controls.WaveMark.Glow, before = 8, after = 12;
        double free = row - title - button - before - after;
        if (free < ProWaveMin + 8) free = row - title - button - before - 4;
        double width = Math.Min(Theme.Metrics.Instance.Size(120), Math.Max(0, free));
        bool show = width >= ProWaveMin;
        if (ProWave.IsVisible != show) ProWave.IsVisible = show;
        if (show && Math.Abs(_proWaveWidth - width) > 0.5)
        {
            _proWaveWidth = width;
            ProWave.WidthRequest = width + 2 * bleed;
        }
    }

    private async void OnDictionaryTapped(object? sender, TappedEventArgs e) => await ChordDictionaryPage.OpenAsync();

    /// <summary>
    /// The page title comes first: it is a large display face and truncating it
    /// looks like a bug. If "Словник" will not fit beside it (a 13 mini), the chip
    /// drops the word and stays an icon button; the tap target is unchanged.
    /// </summary>
    private void OnTitleRowSized(object? sender, EventArgs e)
    {
        double row = TitleRow.Width;
        if (row <= 0) return;
        double title = ((IView)SettingsTitle).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        double word = ((IView)DictionaryLabel).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        double iconChip = Theme.Metrics.Instance.Size(18) + 24;               // icon + chip padding
        bool room = row - title - 10 >= iconChip + 6 + word;
        if (DictionaryLabel.IsVisible != room) DictionaryLabel.IsVisible = room;
    }

    private SettingsViewModel? Vm => BindingContext as SettingsViewModel;

    private static Page? Host => Application.Current?.Windows.FirstOrDefault()?.Page;

    private async void OnDevWindowTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        var names = Services.DevWindow.Presets.Select(p => $"{p.Name} · {p.Width:0}×{p.Height:0}").ToArray();
        string? choice = await Host.DisplayActionSheetAsync(Loc.Get("Settings_DevWindow"), Loc.Get("Common_Cancel"), null, names);
        int i = Array.IndexOf(names, choice);
        if (i < 0) return;
        Services.DevWindow.Apply(Services.DevWindow.Presets[i]);
        Vm.RefreshDevWindow();
    }

    private async void OnThemeTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        string system = Loc.Get("Theme_System"), dark = Loc.Get("Theme_Dark"), light = Loc.Get("Theme_Light");
        string? choice = await Host.DisplayActionSheetAsync(Loc.Get("Settings_Theme"), Loc.Get("Common_Cancel"), null, system, dark, light);
        if (choice == system) Vm.SetTheme(0);
        else if (choice == dark) Vm.SetTheme(1);
        else if (choice == light) Vm.SetTheme(2);
    }

    private async void OnLanguageTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        string uk = Loc.Get("Lang_Uk"), en = Loc.Get("Lang_En");
        string? choice = await Host.DisplayActionSheetAsync(Loc.Get("Settings_Language"), Loc.Get("Common_Cancel"), null, uk, en);
        if (choice == uk) Vm.SetLanguage("uk");
        else if (choice == en) Vm.SetLanguage("en");
    }

    private void OnA4Tapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        if (Vm.A4Locked)
            _ = PaywallSheet.ShowAsync(Feature.A4Reference);
        else
            _ = A4Sheet.ShowAsync();
    }

    private void OnDefaultTuningTapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        _ = TuningSheet.ShowAsync(AppSettings.DefaultTuning, Vm.AltTuningsLocked, picked =>
        {
            if (picked.IsPro && Vm.AltTuningsLocked)
            {
                _ = PaywallSheet.ShowAsync(Feature.AltTunings);
                return false;
            }
            Vm.SetDefaultTuning(picked.Id);
            return true;
        });
    }

    private void OnProTapped(object? sender, EventArgs e)
    {
        Strunika.Core.Diagnostics.FileLog.Info("Settings: Learn more tapped");
        _ = PaywallSheet.ShowAsync(null, push: true);
    }
}
