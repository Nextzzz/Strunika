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
    }

    private SettingsViewModel? Vm => BindingContext as SettingsViewModel;

    private static Page? Host => Application.Current?.Windows.FirstOrDefault()?.Page;

    private async void OnDevWindowTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        var names = Services.DevWindow.Presets.Select(p => $"{p.Name} · {p.Width:0}×{p.Height:0}").ToArray();
        string? choice = await Host.DisplayActionSheet(Loc.Get("Settings_DevWindow"), Loc.Get("Common_Cancel"), null, names);
        int i = Array.IndexOf(names, choice);
        if (i < 0) return;
        Services.DevWindow.Apply(Services.DevWindow.Presets[i]);
        Vm.RefreshDevWindow();
    }

    private async void OnThemeTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        string system = Loc.Get("Theme_System"), dark = Loc.Get("Theme_Dark"), light = Loc.Get("Theme_Light");
        string? choice = await Host.DisplayActionSheet(Loc.Get("Settings_Theme"), Loc.Get("Common_Cancel"), null, system, dark, light);
        if (choice == system) Vm.SetTheme(0);
        else if (choice == dark) Vm.SetTheme(1);
        else if (choice == light) Vm.SetTheme(2);
    }

    private async void OnLanguageTapped(object? sender, TappedEventArgs e)
    {
        if (Host == null || Vm == null) return;
        string uk = Loc.Get("Lang_Uk"), en = Loc.Get("Lang_En");
        string? choice = await Host.DisplayActionSheet(Loc.Get("Settings_Language"), Loc.Get("Common_Cancel"), null, uk, en);
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
