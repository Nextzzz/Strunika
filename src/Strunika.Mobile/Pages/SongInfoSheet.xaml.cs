using Strunika.Core.Diagnostics;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Theme;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Everything known about the song: source, key (as analysed, plus the key it
/// sounds in when transposed), tempo, length, the model that produced the
/// chords, and every chord that occurs in it. Opened by tapping the title
/// block on the song page.
/// </summary>
public partial class SongInfoSheet : ContentPage
{
    private static bool _open;

    private readonly SongViewModel _vm;

    public SongInfoSheet(SongViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        var song = vm.Song;
        TitleLabel.Text = song.Title;
        ArtistLabel.Text = vm.Artist;

        AddFact(Loc.Get("Song_Info_Source"), song.Source switch
        {
            SongSource.YouTube => Loc.Get("Library_Source_YouTube"),
            SongSource.Recording => Loc.Get("Library_Source_Recording"),
            _ => Loc.Get("Library_Source_File"),
        });
        AddFact(Loc.Get("Song_Info_Key"), vm.OriginalKeyText, display: true);
        if (song.Bpm > 0) AddFact(Loc.Get("Song_Info_Tempo"), $"♩ {song.Bpm:0} · 4/4");
        if (song.DurationSec > 0) AddFact(Loc.Get("Song_Info_Length"), SongItem.Duration(song.DurationSec));
        AddFact(Loc.Get("Song_Info_Added"), song.CreatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm"));
        // Which network produced the chords matters to nobody but an expert.
        if (Services.AppSettings.Expert && !string.IsNullOrEmpty(song.Model)) AddFact(Loc.Get("Song_Info_Model"), song.Model);

        foreach (var chord in vm.ChordList)
        {
            string label = chord;
            var chip = new Border
            {
                Style = (Style)Application.Current!.Resources["Chip"],
                HeightRequest = Metrics.Instance.Size(38),
                Padding = new Thickness(14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Content = new Label
                {
                    Text = chord,
                    FontFamily = "Display",
                    FontSize = 17,
                    TextColor = Tokens.Current("AccentText"),
                    VerticalOptions = LayoutOptions.Center,
                },
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await ShowShapesAsync(label);
            chip.GestureRecognizers.Add(tap);
            Chords.Children.Add(chip);
        }
    }

    /// <summary>A chord in the list opens the same shapes sheet the song page uses.</summary>
    private Task ShowShapesAsync(string chord)
    {
        var (positions, index) = _vm.ShapeChoices(chord);
        return ChordShapesSheet.ShowAsync(chord, positions, index, _vm.LeftHanded, _vm.Capo, i => _vm.ChooseShape(chord, i));
    }

    /// <param name="display">Show the value in the display serif (a key).</param>
    /// <param name="quiet">A footnote under the row above, no label column.</param>
    private void AddFact(string label, string value, bool display = false, bool quiet = false)
    {
        if (Facts.Children.Count > 0 && !quiet)
            Facts.Children.Add(new BoxView { Style = (Style)Application.Current!.Resources["SeparatorLine"], Margin = new Thickness(14, 0, 0, 0) });

        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        if (quiet)
        {
            row.Padding = new Thickness(14, 0, 14, 10);
            row.Add(new Label { Text = value, Style = (Style)Application.Current!.Resources["Footnote"] });
        }
        else
        {
            row.Style = (Style)Application.Current!.Resources["Row"];
            row.Add(new Label { Text = label, Style = (Style)Application.Current!.Resources["RowLabel"] });
            var v = new Label { Text = value, Style = (Style)Application.Current!.Resources["RowValue"] };
            if (display) { v.FontFamily = "Display"; v.TextColor = Tokens.Current("AccentText"); }
            row.Add(v, 1, 0);
        }
        Facts.Children.Add(row);
    }

    public static async Task ShowAsync(SongViewModel vm)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new SongInfoSheet(vm), animated: true); }
        catch (Exception ex) { FileLog.Error("SongInfoSheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) => await Navigation.PopModalAsync(animated: true);
}
