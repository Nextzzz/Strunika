using Strunika.Mobile.Localization;
using Strunika.Mobile.Pages;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class LibraryView : ContentView
{
    public LibraryView()
    {
        InitializeComponent();
        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is LibraryViewModel vm)
            {
                vm.ProRequired += (_, feature) => _ = PaywallSheet.ShowAsync(feature);
                vm.Message += (_, text) => _ = ShowMessageAsync(text);
                vm.OpenRequested += (_, item) => _ = OpenAsync(item);
                vm.YouTubeChoiceRequested += (_, link) => _ = YouTubeChoiceSheet.ShowAsync(link, useLink => UseYouTubeAsync(vm, useLink ? link : null));
                vm.BrowseYouTubeRequested += (_, _) => _ = YouTubeBrowserPage.ShowAsync(vm);
                vm.RecordRequested += (_, _) => _ = RecordSheet.ShowAsync(vm);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(LibraryViewModel.PinYouTube) or nameof(LibraryViewModel.PinFile) or nameof(LibraryViewModel.PinRecord))
                        LayoutQuickRow(vm);
                };
                LayoutQuickRow(vm);
                Header.SizeChanged += (_, _) => FitHeader();
                // A page turn starts at the top of the list — header included, which
                // ScrollTo(item) cannot express (Controls/ScrollHelper).
                vm.PageChanged += (_, _) => Controls.ScrollHelper.ToTop(List);
                ApplyShade();
                Services.AppSettings.Changed += (_, key) => { if (key == nameof(Services.AppSettings.Theme)) ApplyShade(); };
                if (Application.Current != null) Application.Current.RequestedThemeChanged += (_, _) => ApplyShade();
                // Width is known only after the first layout; labels change with the language.
                QuickRow.SizeChanged += (_, _) => LayoutQuickRow(vm);
                Localization.Loc.Instance.PropertyChanged += (_, _) => Dispatcher.Dispatch(() => LayoutQuickRow(vm));
            }
        };
    }

    private LibraryViewModel? Vm => BindingContext as LibraryViewModel;

    /// <summary>The list scrolls under the pinned header: its spacer and the
    /// shade follow the header's height (quick row, language, search).</summary>
    private void FitHeader()
    {
        if (Header.Height <= 0) return;
        HeaderSpacer.HeightRequest = Header.Height + 14;
        HeaderShade.Margin = new Thickness(0, Header.Height, 0, 0);
    }

    /// <summary>Short shade under the header: page background dissolving over 28 pt.</summary>
    private void ApplyShade()
    {
        var bg = Theme.Tokens.Current("Bg");
        HeaderShade.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(bg, 0f),
                new GradientStop(bg.WithAlpha(0.7f), 0.4f),
                new GradientStop(bg.WithAlpha(0f), 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }

    private static Page? Host => Application.Current?.Windows.FirstOrDefault()?.Page;

    /// <summary>Pinned ways share the first row equally; unpinned ones leave
    /// no gap. Whenever any label would be cut, the last button moves to a
    /// full-width row of its own, until the ones left in the first row fit
    /// (user request 2026-08-26).</summary>
    private void LayoutQuickRow(LibraryViewModel vm)
    {
        var cards = new (Border Card, bool Pinned)[] { (QuickYouTube, vm.PinYouTube), (QuickFile, vm.PinFile), (QuickRecord, vm.PinRecord) };
        var visible = new List<Border>();
        foreach (var (card, pinned) in cards)
        {
            card.IsVisible = pinned;
            if (pinned) visible.Add(card);
        }
        QuickRow.IsVisible = visible.Count > 0;
        if (visible.Count == 0) return;

        // Natural width of each button = its content (icon + label) + padding.
        var needed = visible.Select(card =>
        {
            var content = card.Content as View;
            double w = content?.Measure(double.PositiveInfinity, double.PositiveInfinity).Width ?? 0;
            return w > 0 ? w + card.Padding.HorizontalThickness + 2 : 0;
        }).ToList();

        int firstRow = visible.Count;
        if (QuickRow.Width > 0)
        {
            while (firstRow > 1)
            {
                double share = (QuickRow.Width - QuickRow.ColumnSpacing * (firstRow - 1)) / firstRow;
                bool fits = true;
                for (int i = 0; i < firstRow; i++)
                    if (needed[i] > share) { fits = false; break; }
                if (fits) break;
                firstRow--;
            }
        }

        QuickRow.ColumnDefinitions.Clear();
        QuickRow.RowDefinitions.Clear();
        for (int i = 0; i < firstRow; i++)
            QuickRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        int rows = 1 + (visible.Count - firstRow);
        for (int r = 0; r < rows; r++)
            QuickRow.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int i = 0; i < visible.Count; i++)
        {
            var card = visible[i];
            bool inFirstRow = i < firstRow;
            Grid.SetRow(card, inFirstRow ? 0 : 1 + (i - firstRow));
            Grid.SetColumn(card, inFirstRow ? i : 0);
            Grid.SetColumnSpan(card, inFirstRow ? 1 : firstRow);
        }
    }

    private static async Task UseYouTubeAsync(LibraryViewModel vm, string? link)
    {
        if (link == null)
        {
            await YouTubeBrowserPage.ShowAsync(vm);
            return;
        }
        var error = await vm.AddYouTubeAsync(link);
        if (error != null)
            await ShowMessageAsync(Loc.Get(error));
    }

    private void OnAddTapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        _ = AddSongSheet.ShowAsync(Vm);
    }

    private async void OnSortTapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null || Host == null) return;
        string date = Loc.Get("Library_Sort_Date"), title = Loc.Get("Library_Sort_Title"), key = Loc.Get("Library_Sort_Key");
        var picked = await Host.DisplayActionSheet(Loc.Get("Library_SortTitle"), Loc.Get("Common_Cancel"), null, date, title, key);
        if (picked == date) Vm.SetSort("date");
        else if (picked == title) Vm.SetSort("title");
        else if (picked == key) Vm.SetSort("key");
    }

    private static Task ShowMessageAsync(string text) =>
        Host?.DisplayAlert(Loc.Get("Tab_Songs"), text, "OK") ?? Task.CompletedTask;

    private static Task OpenAsync(SongItem item) => SongPage.OpenAsync(item.Song);
}
