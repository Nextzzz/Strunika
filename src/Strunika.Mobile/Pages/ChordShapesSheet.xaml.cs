using Strunika.Core.Diagnostics;
using Strunika.Mobile.Controls;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Theme;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Every way to play the chord that was tapped, lowest position first. The
/// song is paused while it is open (the player wants to look at the neck, not
/// chase the music); picking one keeps it for that chord until the capo,
/// transposition or "simple chords" changes.
/// </summary>
public partial class ChordShapesSheet : ContentPage
{
    private static bool _open;
    private readonly Action<int> _onPick;
    private bool _picked;

    public ChordShapesSheet(string chord, IReadOnlyList<ChordShape> positions, int index, bool leftHanded, Action<int> onPick)
    {
        InitializeComponent();
        _onPick = onPick;
        ChordLabel.Text = chord;
        if (positions.Count <= 1) Hint.Text = Loc.Get("Song_Alt_Only");

        for (int i = 0; i < positions.Count; i++)
        {
            int chosen = i;
            var shape = positions[i];
            var m = Theme.Metrics.Instance;
            var card = new Border
            {
                WidthRequest = m.Size(104),
                HeightRequest = m.Size(150),
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(6, 8),
                StrokeThickness = i == index ? 2 : 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Stroke = Tokens.Current(i == index ? "Accent" : "Separator"),
                BackgroundColor = Tokens.Current(i == index ? "Surface2" : "Surface1"),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new ChordDiagram
                        {
                            Shape = shape,
                            ShowFrets = true,
                            LeftHanded = leftHanded,
                            HeightRequest = m.Size(108),
                            LineColor = Tokens.Current("TextPri"),
                            DotColor = Tokens.Current("Accent"),
                            MutedColor = Tokens.Current("Dim"),
                            FretTextColor = Tokens.Current("TextSec"),
                        },
                        new Label
                        {
                            Text = shape.BaseFret == 1 ? Loc.Get("Song_Alt_Open") : string.Format(Loc.Get("Song_Fret"), shape.BaseFret),
                            FontSize = 12,
                            HorizontalTextAlignment = TextAlignment.Center,
                            TextColor = Tokens.Current(i == index ? "AccentText" : "TextSec"),
                        },
                    },
                },
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await PickAsync(chosen);
            card.GestureRecognizers.Add(tap);
            Options.Children.Add(card);
        }
    }

    public static async Task ShowAsync(string chord, IReadOnlyList<ChordShape> positions, int index, bool leftHanded, Action<int> onPick)
    {
        if (_open || positions.Count == 0) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new ChordShapesSheet(chord, positions, index, leftHanded, onPick), animated: true); }
        catch (Exception ex) { FileLog.Error("ChordShapesSheet failed", ex); }
        finally { _open = false; }
    }

    private async Task PickAsync(int index)
    {
        if (_picked) return;
        _picked = true;
        _onPick(index);
        await Navigation.PopModalAsync(animated: true);
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_picked) return;
        _picked = true;
        await Navigation.PopModalAsync(animated: true);
    }
}
