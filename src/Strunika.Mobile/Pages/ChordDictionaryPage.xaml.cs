using Strunika.Core.Diagnostics;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Every chord the recogniser can name, grouped by root, searchable, with the
/// "simple" filter shared with the rest of the app. A card shows the lowest
/// shape; tapping one opens the sheet with all its positions. Pushed from
/// Settings, so it slides in from the right and swipes back on iPhone.
/// </summary>
public partial class ChordDictionaryPage : ContentPage
{
    /// <summary>Card width the span is computed from (plus the 10 pt gap).</summary>
    private const double CardWidth = 118;

    private readonly ChordDictionaryViewModel _vm = new();
    private int _span;

    public ChordDictionaryPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        SizeChanged += (_, _) => ApplySpan();
        Unloaded += (_, _) => _vm.Detach();
    }

    public static async Task OpenAsync()
    {
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        try { await host.Navigation.PushAsync(new ChordDictionaryPage(), animated: true); }
        catch (Exception ex) { FileLog.Error("ChordDictionaryPage failed", ex); }
    }

    /// <summary>As many cards per row as fit: two on a compact phone, up to six
    /// on a tablet — the cards keep their size, the grid gains columns.</summary>
    private void ApplySpan()
    {
        double usable = Width - Theme.Metrics.Instance.ContentInset.HorizontalThickness - 40;
        if (usable <= 0) return;
        int span = Math.Clamp((int)((usable + 10) / (Theme.Metrics.Instance.Size(CardWidth) + 10)), 2, 6);
        if (span == _span) return;
        _span = span;
        Layout.Span = span;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Navigation.PopAsync(animated: true);

    private void OnSimpleTapped(object? sender, TappedEventArgs e) => _vm.Simple = !_vm.Simple;

    private async void OnChordTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ChordEntry entry) return;
        var positions = ChordShapes.Positions(entry.Label);
        await ChordShapesSheet.ShowAsync(entry.Label, positions, 0, AppSettings.LeftHanded, capo: 0, onPick: null);
    }
}
