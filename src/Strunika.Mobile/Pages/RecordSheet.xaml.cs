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

    private bool _started;

    /// <summary>The sheet opens armed, not recording: the person starts the
    /// take when they are ready (user decision 2026-09-07).</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _recorder.Level += OnLevel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
        _recorder.Level -= OnLevel;
        if (_started && !_done) _recorder.Cancel();
    }

    private async Task StartAsync()
    {
        if (!await _recorder.StartAsync())
        {
            ErrorLabel.Text = Loc.Get("Library_NoMic");
            ErrorLabel.IsVisible = true;
            StopButton.IsEnabled = false;
            return;
        }
        _started = true;
        RecordingBadge.IsVisible = true;                        // the red "Recording" means it: only from here
        Hint.Text = Loc.Get("Record_Hint");
        StopButton.Text = Loc.Get("Record_Stop");
        _timer.Start();
        if (!Motion.Reduced)
            _ = PulseAsync();
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

    private async void OnMainTapped(object? sender, EventArgs e)
    {
        if (_done) return;
        if (!_started) { await StartAsync(); return; }
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
        if (_started) _recorder.Cancel();
        await Navigation.PopModalAsync(animated: true);
    }
}
