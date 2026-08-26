using Strunika.Mobile.Controls;
using Strunika.Mobile.Models;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Picker for tuning presets. Non-standard tunings show a lock when the
/// user has no Pro; picking one then hands the decision back to the
/// caller (who opens the paywall) and keeps the sheet open.
/// </summary>
public partial class TuningSheet : ContentPage
{
    private static bool _open;
    private readonly Func<Tuning, bool> _onPick;

    public TuningSheet(string currentId, bool altLocked, Func<Tuning, bool> onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        foreach (var tuning in Tuning.All)
            List.Add(BuildRow(tuning, tuning.Id == currentId, altLocked && tuning.IsPro));
    }

    private View BuildRow(Tuning tuning, bool selected, bool locked)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var text = new VerticalStackLayout { Spacing = 2 };
        text.Add(new Label { Text = tuning.Name, FontSize = 16, FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None });
        // Sheet is short-lived: resolve theme colours once instead of binding.
        var caption = new Label { Text = tuning.StringsCaption, FontSize = 13, CharacterSpacing = 1.5, TextColor = Tokens.Current("TextSec") };
        text.Add(caption);
        grid.Add(text, 0, 0);

        if (selected)
            grid.Add(new IconView { Name = "check", Size = 20, ThemeKey = "AccentText", VerticalOptions = LayoutOptions.Center }, 1, 0);
        else if (locked)
            grid.Add(new IconView { Name = "lock", Size = 18, ThemeKey = "AccentText", VerticalOptions = LayoutOptions.Center }, 1, 0);

        var row = new Border
        {
            StrokeThickness = selected ? 1.5 : 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = 0,
            Content = grid,
            BackgroundColor = Tokens.Current("Surface1"),
            Stroke = selected ? Tokens.Current("Accent") : Colors.Transparent,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (_onPick(tuning))
                await Navigation.PopModalAsync(animated: true);
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    public static async Task ShowAsync(string currentId, bool altLocked, Func<Tuning, bool> onPick)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new TuningSheet(currentId, altLocked, onPick), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("TuningSheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PopModalAsync(animated: true);
}
