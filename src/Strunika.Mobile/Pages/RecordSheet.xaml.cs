using Strunika.Mobile.Localization;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// Records a take from the microphone (timer + level), then hands the WAV
/// to the library for analysis.
/// </summary>
public partial class RecordSheet : ContentPage
{
    private static bool _open;
    private readonly LibraryViewModel _vm;
    private readonly TakeRecorder _recorder;
    private readonly IDispatcherTimer _timer;
    private bool _done;

    public RecordSheet(LibraryViewModel vm, TakeRecorder recorder)
    {
        InitializeComponent();
        _vm = vm;
        _recorder = recorder;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(200);
        _timer.Tick += (_, _) => Timer.Text = SongItem.Duration(_recorder.Elapsed.TotalSeconds);
    }

    public static async Task ShowAsync(LibraryViewModel vm)
    {
        if (_open) return;
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        var recorder = host?.Handler?.MauiContext?.Services.GetService<TakeRecorder>();
        if (host == null || recorder == null) return;
        _open = true;
        try { await host.Navigation.PushModalAsync(new RecordSheet(vm, recorder), animated: true); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("RecordSheet failed", ex); }
        finally { _open = false; }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _recorder.Level += OnLevel;
        if (!await _recorder.StartAsync())
        {
            ErrorLabel.Text = Loc.Get("Library_NoMic");
            ErrorLabel.IsVisible = true;
            StopButton.IsEnabled = false;
            return;
        }
        _timer.Start();
        if (!Motion.Reduced)
            _ = PulseAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
        _recorder.Level -= OnLevel;
        if (!_done) _recorder.Cancel();
    }

    private async Task PulseAsync()
    {
        while (_recorder.IsRecording && !_done)
        {
            await Dot.FadeTo(0.25, 600, Easing.SinInOut);
            await Dot.FadeTo(1, 600, Easing.SinInOut);
        }
    }

    private void OnLevel(float peak) => MainThread.BeginInvokeOnMainThread(() => Meter.Push(peak));

    private async void OnStopTapped(object? sender, EventArgs e)
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        var take = _recorder.Stop();
        await Navigation.PopModalAsync(animated: true);
        if (take is { } t)
            await _vm.AddRecordingAsync(t.Path, t.Seconds);
    }

    private async void OnCancelTapped(object? sender, TappedEventArgs e)
    {
        _done = true;
        _recorder.Cancel();
        await Navigation.PopModalAsync(animated: true);
    }
}
