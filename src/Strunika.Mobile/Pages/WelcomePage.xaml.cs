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
}
