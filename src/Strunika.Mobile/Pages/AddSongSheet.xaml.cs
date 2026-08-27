using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// "+" on the Songs tab: YouTube, a file, or a recording. Each way carries a
/// star — starred ways also sit at the top of the Songs screen. The YouTube
/// row behaves like the quick button: clipboard link → choice sheet,
/// otherwise the built-in YouTube.
/// </summary>
public partial class AddSongSheet : ContentPage
{
    private static bool _open;
    private readonly LibraryViewModel _vm;
    private bool _busy;

    public AddSongSheet(LibraryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var caption = await _vm.QuotaCaptionAsync();
            QuotaLabel.Text = caption;
            QuotaLabel.IsVisible = caption.Length > 0;
        }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("add sheet", ex); }
    }

    public static async Task ShowAsync(LibraryViewModel vm)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (host == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new AddSongSheet(vm), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("AddSongSheet failed", ex); }
        finally { _open = false; }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e) => await CloseAsync();

    private Task CloseAsync() => Navigation.ModalStack.Contains(this) ? Navigation.PopModalAsync(animated: true) : Task.CompletedTask;

    /// <summary>Every way opens a sheet or picker of its own: close this one first.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await CloseAsync();
            await action();
        }
        finally { _busy = false; }
    }

    private async void OnYouTubeTapped(object? sender, TappedEventArgs e) => await RunAsync(_vm.YouTubeTapAsync);

    private async void OnFileTapped(object? sender, TappedEventArgs e) => await RunAsync(_vm.AddFileAsync);

    private async void OnRecordTapped(object? sender, TappedEventArgs e) => await RunAsync(() => RecordSheet.ShowAsync(_vm));
}
