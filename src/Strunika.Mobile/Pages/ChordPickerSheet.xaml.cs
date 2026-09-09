using Strunika.Core.Diagnostics;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Which chord it should be: the root first, then what is built on it. Two
/// dozen chips instead of a wall of two hundred, one tap each, and the root
/// the song already has is the one open when the sheet arrives.
/// <para>Always the whole vocabulary (<see cref="ChordCatalogue"/>), never the
/// simple one: the editor is behind Pro, so the reader has all of it, and the
/// song page shows the chords unsimplified while the editor is on.</para>
/// <para>Where a chord is being replaced the sheet asks first whether every
/// chord of that name in the song goes with it, so the answer is already given
/// by the time one is tapped.</para>
/// </summary>
public partial class ChordPickerSheet : ContentPage
{
    private static bool _open;
    private readonly Func<string, bool, Task> _onPick;
    private readonly string _current;
    private readonly List<Border> _rootChips = new();
    private string _root;
    private bool _done;

    private ChordPickerSheet(string current, bool offerAll, Func<string, bool, Task> onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        _current = current is null or "—" ? "" : current;
        Now.Text = _current;
        if (offerAll && _current.Length > 0)
        {
            AllRow.IsVisible = true;
            AllLabel.Text = string.Format(Loc.Get("Song_Editor_All"), _current);
        }

        _root = RootOf(_current) ?? ChordCatalogue.Roots[0];
        foreach (var root in ChordCatalogue.Roots)
        {
            string name = root;
            var chip = Chip(root, name == _root, wide: false);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ShowRoot(name);
            chip.GestureRecognizers.Add(tap);
            _rootChips.Add(chip);
            RootRow.Add(chip);
        }
        ShowRoot(_root);
    }

    /// <summary>The root of a chord name ("F#m7" gives "F#"), or null when it is not one.</summary>
    private static string? RootOf(string chord)
    {
        if (chord.Length == 0) return null;
        string two = chord.Length > 1 ? chord[..2] : "";
        if (ChordCatalogue.Roots.Contains(two)) return two;
        string one = chord[..1];
        return ChordCatalogue.Roots.Contains(one) ? one : null;
    }

    private void ShowRoot(string root)
    {
        _root = root;
        for (int i = 0; i < _rootChips.Count; i++) Paint(_rootChips[i], ChordCatalogue.Roots[i] == root);

        QualityRow.Clear();
        foreach (var quality in ChordCatalogue.Qualities)
        {
            string label = root + quality;
            var chip = Chip(label, label == _current, wide: true);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await PickAsync(label);
            chip.GestureRecognizers.Add(tap);
            QualityRow.Add(chip);
        }
    }

    private static Border Chip(string text, bool on, bool wide)
    {
        var chip = new Border
        {
            Style = (Style)Application.Current!.Resources["Chip"],
            HeightRequest = Metrics.Instance.Size(44, min: 44),
            MinimumWidthRequest = Metrics.Instance.Size(wide ? 62 : 52, min: wide ? 62 : 52),
            Padding = new Thickness(12, 0),
            Margin = new Thickness(0, 0, 8, 8),
            Content = new Label
            {
                Text = text,
                FontFamily = "Display",
                FontSize = 18,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        Paint(chip, on);
        return chip;
    }

    private static void Paint(Border chip, bool on)
    {
        chip.BackgroundColor = on ? Tokens.Current("Fill") : Tokens.Current("Surface1");
        chip.Stroke = on ? Tokens.Current("Fill") : Tokens.Current("Separator");
        if (chip.Content is Label label) label.TextColor = Tokens.Current(on ? "OnFill" : "TextPri");
    }

    private async Task PickAsync(string label)
    {
        if (_done) return;
        _done = true;
        bool everywhere = AllRow.IsVisible && AllSwitch.IsToggled;
        try { Haptics.Default.Selection(); } catch { /* no engine */ }
        await Navigation.PopModalAsync(animated: true);
        await _onPick(label, everywhere);
    }

    /// <param name="offerAll">Replacing a chord: offer to replace every one of its name.</param>
    public static async Task ShowAsync(string current, bool offerAll, Func<string, bool, Task> onPick)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new ChordPickerSheet(current, offerAll, onPick), animated: true); }
        catch (Exception ex) { FileLog.Error("ChordPickerSheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_done) return;
        _done = true;
        await Navigation.PopModalAsync(animated: true);
    }
}
