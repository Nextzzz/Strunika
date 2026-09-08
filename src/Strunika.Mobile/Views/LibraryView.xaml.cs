using Strunika.Mobile.Theme;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pages;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class LibraryView : ContentView
{
    /// <summary>Which cards are past the delete line, so the buzz fires once per crossing.</summary>
    private readonly HashSet<SwipeView> _pastLine = new();

    private void OnSwipeChanging(object? sender, SwipeChangingEventArgs e)
    {
        if (sender is not SwipeView view) return;
        bool past = Math.Abs(e.Offset) >= Theme.Metrics.Instance.SwipeThreshold;
        bool was = _pastLine.Contains(view);
        if (past == was) return;
        if (past) _pastLine.Add(view); else _pastLine.Remove(view);
        // The line is crossed: a buzz, whichever way (letting go now deletes / no longer deletes).
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { /* no engine */ }
    }

    /// <summary>Letting go past the line deletes. MAUI's own threshold is the
    /// slab's whole width (that is what lets the card travel), so the decision
    /// is taken here from where the finger left off.</summary>
    private void OnSwipeEnded(object? sender, SwipeEndedEventArgs e)
    {
        if (sender is not SwipeView view) return;
        bool past = _pastLine.Remove(view);
        view.Close();
        if (past && view.BindingContext is ViewModels.SongItem item && BindingContext is ViewModels.LibraryViewModel vm
            && vm.DeleteCommand.CanExecute(item))
            vm.DeleteCommand.Execute(item);
    }

    /// <summary>A card scrolled under the home indicator must not pad itself by
    /// it (the list runs edge to edge under the floating bar).</summary>
    private void OnCardLoaded(object? sender, EventArgs e)
    {
#if IOS
        if (sender is Element card) Theme.SafeArea.IgnoreBelow(card);
#endif
    }

    /// <summary>A recycled card shows another song: whatever the last one was
    /// doing at the delete line is forgotten.</summary>
    private void OnCardRebound(object? sender, EventArgs e)
    {
        if (sender is SwipeView view) _pastLine.Remove(view);
    }

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
                // A page turn, a filter, a sort or a search starts at the top of the
                // list; nothing else moves it (the list keeps its own offset when
                // songs come and go, and the page is patched in place, never
                // rebuilt — LibraryViewModel.ApplyPage).
                vm.ListReset += (_, _) => ScrollToTop();
                // Width is known only after the first layout; labels change with the
                // language and sizes with the size class (Theme.Refit covers both).
                QuickRow.SizeChanged += (_, _) => LayoutQuickRow(vm);
                Theme.Refit.Watch(this, () => LayoutQuickRow(vm));
            }
        };
#if IOS
        // The footer runs under the home indicator like the cards; padded by it,
        // it would grow at the end of every scroll and the list would jump.
        Theme.SafeArea.IgnoreBelow(Footer);
#endif
    }

    private LibraryViewModel? Vm => BindingContext as LibraryViewModel;

    /// <summary>The first card to the top of the list, once the new items are in
    /// place. There is no header inside the list any more, so the first item's
    /// top is the top. With nothing to show the list is at its top already.</summary>
    private void ScrollToTop()
    {
        Dispatcher.Dispatch(() =>
        {
            if (Vm is { Items.Count: > 0 })
                List.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            HeaderShade.Follow(0);
        });
    }

    /// <summary>The shade under the header is there only while cards are under it (Controls/ScrollShade).</summary>
    private void OnListScrolled(object? sender, ItemsViewScrolledEventArgs e) => HeaderShade.Follow(e.VerticalOffset);

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
        var picked = await Host.DisplayActionSheetAsync(Loc.Get("Library_SortTitle"), Loc.Get("Common_Cancel"), null, date, title, key);
        if (picked == date) Vm.SetSort("date");
        else if (picked == title) Vm.SetSort("title");
        else if (picked == key) Vm.SetSort("key");
    }

    private static Task ShowMessageAsync(string text) =>
        Host?.DisplayAlertAsync(Loc.Get("Tab_Songs"), text, "OK") ?? Task.CompletedTask;

    private static Task OpenAsync(SongItem item) => SongPage.OpenAsync(item.Song);
}
