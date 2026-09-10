using Strunika.Core.Diagnostics;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Which chord it should be: the root first, then what is built on it, and the
/// choice is shown as a diagram before it is made. Two dozen keys instead of a
/// wall of two hundred, and the root is a choice of its own that looks like one
/// — a rail of round keys, nothing like the chord chips under it, which is what
/// made the two impossible to tell apart before (user report 2026-09-10).
/// <para>Always the whole vocabulary (<see cref="ChordCatalogue"/>), never the
/// simple one: the editor is behind Pro, so the reader has all of it, and the
/// song page shows the chords unsimplified while the editor is on.</para>
/// <para>Where a chord is being replaced the sheet asks whether every chord of
/// that name in the song goes with it. Nothing happens until the button at the
/// bottom is pressed.</para>
/// </summary>
public partial class ChordPickerSheet : ContentPage
{
    private static bool _open;
    private readonly Func<string, bool, Task> _onPick;
    private readonly string _current;
    private readonly List<Border> _rootKeys = new();
    private readonly List<Border> _qualityChips = new();
    private string _root, _chosen;
    private bool _done;

    private ChordPickerSheet(string current, bool offerAll, Func<string, bool, Task> onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        _current = current is null or "—" ? "" : current;
        _chosen = _current;
        Confirm.Text = Loc.Get(offerAll ? "Song_Editor_Replace" : "Song_Editor_Insert");
        if (offerAll && _current.Length > 0)
        {
            AllRow.IsVisible = true;
            AllLabel.Text = string.Format(Loc.Get("Song_Editor_All"), _current);
        }

        _root = RootOf(_current) ?? ChordCatalogue.Roots[0];
        foreach (var root in ChordCatalogue.Roots)
        {
            string name = root;
            var key = Key(root, name == _root);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ShowRoot(name);
            key.GestureRecognizers.Add(tap);
            _rootKeys.Add(key);
            RootRow.Add(key);
        }
        ShowRoot(_root);

        if (Application.Current?.Windows.FirstOrDefault()?.Page?.Handler?.MauiContext?.Services.GetService<IChordAudio>() is { } audio)
        {
            void Finished(object? _, EventArgs __) => Ringing(false);
            audio.Finished += Finished;
            Unloaded += (_, _) => { audio.Finished -= Finished; if (_ringing) audio.Stop(); };
        }
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
        for (int i = 0; i < _rootKeys.Count; i++) Paint(_rootKeys[i], ChordCatalogue.Roots[i] == root, key: true);

        QualityRow.Clear();
        _qualityChips.Clear();
        foreach (var quality in ChordCatalogue.Qualities)
        {
            string label = root + quality;
            var chip = Chip(label, label == _chosen);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Choose(label);
            chip.GestureRecognizers.Add(tap);
            _qualityChips.Add(chip);
            QualityRow.Add(chip);
        }
        // A new root and nothing chosen on it yet: the plain triad, so the
        // diagram always shows something and one tap is enough.
        if (RootOf(_chosen) != root) Choose(root);
        else Show(_chosen);
    }

    private void Choose(string label)
    {
        _chosen = label;
        for (int i = 0; i < _qualityChips.Count; i++)
            Paint(_qualityChips[i], _root + ChordCatalogue.Qualities[i] == label, key: false);
        Show(label);
        try { Haptics.Default.Selection(); } catch { /* no engine */ }
    }

    private void Show(string label)
    {
        Chosen.Text = label;
        Preview.Shape = ChordShapes.For(label);
        Preview.LeftHanded = AppSettings.LeftHanded;
    }

    /// <summary>Hear the chord before choosing it: the same strum the shapes
    /// sheet plays, at the same level under the song's.</summary>
    private void OnHearTapped(object? sender, TappedEventArgs e)
    {
        var audio = Application.Current?.Windows.FirstOrDefault()?.Page?.Handler?.MauiContext?.Services.GetService<IChordAudio>();
        var shape = Preview.Shape;
        if (audio == null || shape == null) return;
        if (_ringing) { audio.Stop(); return; }
        audio.Volume = AppSettings.SongVolume * 0.75;
        audio.Strum(shape.Frets);
        Ringing(true);
    }

    private bool _ringing;

    private void Ringing(bool ringing)
    {
        _ringing = ringing;
        HearIcon.Name = ringing ? "pause" : "play";
        HearIcon.Margin = ringing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
    }

    private static Border Key(string text, bool on)
    {
        double size = Metrics.Instance.Size(46, min: 44);
        var key = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 1,
            Padding = 0,
            Margin = new Thickness(0, 0, 6, 6),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
            Content = new Label
            {
                Text = text,
                FontFamily = "DisplayBold",
                FontSize = 17,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };
        Paint(key, on, key: true);
        return key;
    }

    private static Border Chip(string text, bool on)
    {
        var chip = new Border
        {
            Style = (Style)Application.Current!.Resources["Chip"],
            HeightRequest = Metrics.Instance.Size(44, min: 44),
            MinimumWidthRequest = Metrics.Instance.Size(66, min: 62),
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
        Paint(chip, on, key: false);
        return chip;
    }

    private static void Paint(Border chip, bool on, bool key)
    {
        chip.BackgroundColor = on ? Tokens.Current("Fill") : Tokens.Current(key ? "Surface2" : "Surface1");
        chip.Stroke = on ? Tokens.Current("Fill") : Tokens.Current("Separator");
        if (chip.Content is Label label) label.TextColor = Tokens.Current(on ? "OnFill" : "TextPri");
    }

    private async void OnConfirm(object? sender, EventArgs e)
    {
        if (_done || _chosen.Length == 0) return;
        _done = true;
        bool everywhere = AllRow.IsVisible && AllSwitch.IsToggled;
        await Navigation.PopModalAsync(animated: true);
        await _onPick(_chosen, everywhere);
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
