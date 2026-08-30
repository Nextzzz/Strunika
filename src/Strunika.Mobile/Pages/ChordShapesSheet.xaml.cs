using Strunika.Core.Diagnostics;
using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Every way to play a chord. The position being looked at is drawn large in
/// the middle; the row underneath holds all of them. Opened from the song page
/// it ends with "Choose" and keeps the pick for that chord (the song is paused
/// meanwhile — the player wants to look at the neck, not chase the music);
/// opened from the dictionary it is a reference, with no button and no hint.
/// </summary>
public partial class ChordShapesSheet : ContentPage
{
    private static bool _open;
    private readonly Action<int>? _onPick;
    private readonly IReadOnlyList<ChordShape> _positions;
    private readonly List<Border> _cards = new();
    private readonly int _capo;
    private int _selected;
    private bool _done, _ringing;

    /// <summary>The preview strum sits under the song's own level: it is a
    /// reference, not the performance (user request 2026-08-28).</summary>
    private const double StrumLevel = 0.75;

    /// <param name="onPick">null when the sheet is only a reference (the dictionary).</param>
    public ChordShapesSheet(string chord, IReadOnlyList<ChordShape> positions, int index, bool leftHanded, int capo, Action<int>? onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        _positions = positions;
        _capo = capo;
        _selected = Math.Clamp(index, 0, Math.Max(0, positions.Count - 1));
        ChordLabel.Text = chord;
        Preview.LeftHanded = leftHanded;
        Preview.Capo = capo;

        bool picking = onPick != null;
        PickButton.IsVisible = picking;
        Hint.IsVisible = picking;
        if (picking && positions.Count <= 1) Hint.Text = Loc.Get("Song_Alt_Only");

        for (int i = 0; i < positions.Count; i++)
        {
            int option = i;
            var card = new Border
            {
                WidthRequest = Metrics.Instance.Size(88),
                HeightRequest = Metrics.Instance.Size(126),
                Padding = new Thickness(4, 6),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                // The caption takes its own height first and the diagram fills what
                // is left: a stack with a fixed diagram height clipped the caption on
                // a compact phone, where the card scales but the font does not.
                Content = BuildCard(positions[i], leftHanded, capo, Caption(positions[i])),
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Select(option);
            card.GestureRecognizers.Add(tap);
            _cards.Add(card);
            Options.Children.Add(card);
        }
        Select(_selected);

        if (Audio is { } audio)
        {
            void OnFinished(object? s, EventArgs e) => SetRinging(false);
            audio.Finished += OnFinished;
            Unloaded += (_, _) => { audio.Finished -= OnFinished; if (_ringing) audio.Stop(); };
        }
    }

    /// <summary>A card: the caption takes its own height first and the diagram
    /// fills what is left. A stack with a fixed diagram height clipped the caption
    /// on a compact phone, where the card scales but the font does not.</summary>
    private static Grid BuildCard(ChordShape shape, bool leftHanded, int capo, string caption)
    {
        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            RowSpacing = 2,
        };
        grid.Add(new ChordDiagram
        {
            Shape = shape,
            LeftHanded = leftHanded,
            Capo = capo,
            VerticalOptions = LayoutOptions.Fill,
            LineColor = Tokens.Current("TextPri"),
            DotColor = Tokens.Current("Accent"),
            MutedColor = Tokens.Current("Dim"),
            FretTextColor = Tokens.Current("TextSec"),
        }, 0, 0);
        grid.Add(new Label
        {
            Text = caption,
            FontSize = 12,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Tokens.Current("TextSec"),
        }, 0, 1);
        return grid;
    }

    /// <param name="full">The big preview has room for the whole phrase; a card does not.</param>
    private string Caption(ChordShape shape, bool full = false)
    {
        bool open = shape.BaseFret == 1 && _capo == 0;
        if (open) return Loc.Get(full ? "Song_Alt_Open" : "Song_Alt_Open_Short");
        return string.Format(Loc.Get("Song_Fret"), shape.BaseFret + _capo);
    }

    /// <summary>Hear the position: the shape is strummed low string to high,
    /// with the capo counted in, at the song volume. While it rings the button
    /// stops it instead of starting it again.</summary>
    private void OnStrumTapped(object? sender, TappedEventArgs e)
    {
        if (_selected < 0 || _selected >= _positions.Count) return;
        var audio = Audio;
        if (audio == null) return;
        if (_ringing) { audio.Stop(); return; }
        var frets = _positions[_selected].Frets.Select(f => f < 0 ? -1 : f + _capo).ToArray();
        audio.Volume = AppSettings.SongVolume * StrumLevel;
        audio.Strum(frets);
        SetRinging(true);
    }

    private IChordAudio? Audio =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.Handler?.MauiContext?.Services.GetService<IChordAudio>();

    private void SetRinging(bool ringing)
    {
        _ringing = ringing;
        StrumIcon.Name = ringing ? "pause" : "play";
        StrumIcon.Margin = ringing ? new Thickness(0) : new Thickness(3, 0, 0, 0);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _positions.Count) return;
        if (_ringing) Audio?.Stop();                             // a different position: silence the old one
        _selected = index;
        Preview.Shape = _positions[index];
        PreviewCaption.Text = Caption(_positions[index], full: true);
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = i == index;
            _cards[i].Stroke = Tokens.Current(on ? "Accent" : "Separator");
            _cards[i].StrokeThickness = on ? 2 : 1;
            _cards[i].BackgroundColor = Tokens.Current(on ? "Surface2" : "Surface1");
        }
    }

    public static async Task ShowAsync(string chord, IReadOnlyList<ChordShape> positions, int index, bool leftHanded, int capo, Action<int>? onPick)
    {
        if (_open || positions.Count == 0) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new ChordShapesSheet(chord, positions, index, leftHanded, capo, onPick), animated: true); }
        catch (Exception ex) { FileLog.Error("ChordShapesSheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnPickClicked(object? sender, EventArgs e)
    {
        if (_done) return;
        _done = true;
        _onPick?.Invoke(_selected);
        await Navigation.PopModalAsync(animated: true);
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_done) return;
        _done = true;
        await Navigation.PopModalAsync(animated: true);
    }
}
