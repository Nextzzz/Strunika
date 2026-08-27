using System.Diagnostics;
using Strunika.Core.Diagnostics;
using Strunika.Mobile.Data;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Pages;

/// <summary>
/// The song screen (M3): the chord now playing with its diagram, the one
/// coming next beside it, the waveform conveyor, a position slider and the
/// transport. Pushed from the library (slides in from the right).
/// </summary>
public partial class SongPage : ContentPage
{
    private readonly SongViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly IDispatcherTimer _scrubEnd;
    private readonly Stopwatch _clock = new();
    private bool _sliderFromCode, _sliderScrubbing;
    /// <summary>A sheet is covering the page: it disappears, but it must keep
    /// its player — otherwise coming back reloads the song from the start.</summary>
    private bool _sheetOpen, _attached;
    private int _frame;

    public SongPage(Song song, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _vm = new SongViewModel(song, services.GetRequiredService<ISongRepository>(), services.GetRequiredService<IProGate>(), services.GetRequiredService<IClickPlayer>());
        BindingContext = _vm;
        _vm.ProRequired += (_, f) => { _sheetOpen = true; _ = PaywallSheet.ShowAsync(f); };
        _vm.Message += (_, text) => _ = DisplayAlert(song.Title, text, "OK");

        Track.ScrubStarted += (_, _) => _ = _vm.ScrubStartAsync();
        Track.Scrubbing += (_, t) => _vm.Scrubbing(t);
        Track.ScrubEnded += (_, t) => _ = _vm.ScrubEndAsync(t);
        Track.SeekRequested += (_, t) => _ = _vm.SeekAsync(t);

        // The Slider's DragCompleted does not fire for every input on every
        // platform (a mouse wheel or a keyboard nudge on the dev head), so a
        // short idle timer always finishes the scrub.
        _scrubEnd = Dispatcher.CreateTimer();
        _scrubEnd.Interval = TimeSpan.FromMilliseconds(280);
        _scrubEnd.Tick += (_, _) => EndSliderScrub();

        ApplyPanelSpacing(around: false);

        if (!string.IsNullOrEmpty(song.ThumbnailPath))
            Thumb.Source = Path.Combine(FileSystem.AppDataDirectory, song.ThumbnailPath);
    }

    public static async Task OpenAsync(Song song)
    {
        var host = Application.Current?.Windows.FirstOrDefault()?.Page;
        var services = host?.Handler?.MauiContext?.Services;
        if (host == null || services == null) return;
        try { await host.Navigation.PushAsync(new SongPage(song, services), animated: true); }
        catch (Exception ex) { FileLog.Error("SongPage failed", ex); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_attached)
        {
            _sheetOpen = false;                                  // back from a sheet: keep playing where we stood
            _clock.Restart();
            StartFrames();
            return;
        }
        try
        {
            if (_vm.IsYouTube)
            {
                Player.PlayerError += OnPlayerError;
                await Player.LoadAsync(_vm.Song.SourceRef);
                _vm.Attach(new YouTubeTransport(Player, _vm.Song.DurationSec));
                await SetPlayerExpandedAsync(true, animate: false);   // the official player is in view from the start
            }
            else
            {
                var player = _services.GetRequiredService<IAudioPlayer>();
                await player.LoadAsync(Path.Combine(FileSystem.AppDataDirectory, _vm.Song.SourceRef));
                _vm.Attach(new FileTransport(player));
                // Songs analysed before M3 have no waveform yet.
                _ = Task.Run(() => _vm.EnsurePeaksAsync(_services.GetRequiredService<IAudioDecoder>()));
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("song open", ex);
            await DisplayAlert(_vm.Title, Loc.Get("Library_Err_File"), "OK");
        }
        _attached = true;
        _clock.Restart();
        StartFrames();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.AbortAnimation(FramesHandle);
        _scrubEnd.Stop();
        _clock.Stop();
        if (_sheetOpen) return;                                  // only paused, not gone
        _attached = false;
        _vm.Dispose();
    }

    /// <summary>
    /// The conveyor is redrawn on the platform's own frame ticker (vsync), not
    /// on a dispatcher timer — a timer drifts against the compositor and that
    /// is what read as stutter. The position it draws is predicted between
    /// transport probes (see <see cref="SongViewModel.Frame"/>).
    /// </summary>
    private const string FramesHandle = "songFrames";

    private void StartFrames() =>
        new Animation(_ => OnFrame()).Commit(this, FramesHandle, length: 16, repeat: () => true);

    private void OnFrame()
    {
        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        try
        {
            _vm.Frame(Math.Min(dt, 0.25));                       // a stalled frame must not jump the song
            // The conveyor is driven directly, not through a binding: the
            // binding machinery on every frame is exactly what made it stutter.
            if (Math.Abs(Track.Position - _vm.Position) > 0.002) Track.Position = _vm.Position;
            // The slider is a native control — nudging it 60 times a second
            // costs a WinUI layout pass each time and gains nothing.
            if (!_sliderScrubbing && ++_frame % 6 == 0)
            {
                _sliderFromCode = true;
                Seeker.Value = Math.Clamp(_vm.Position, 0, Seeker.Maximum);
                _sliderFromCode = false;
            }
        }
        catch (Exception ex) { FileLog.Error("song frame", ex); }
    }

    /// <summary>101/150 = the owner disallows embedding, 153 = the page had no
    /// usable origin. Either way the only honest offer is YouTube itself.</summary>
    private async void OnPlayerError(object? sender, int code)
    {
        var open = await DisplayAlert(_vm.Title, string.Format(Loc.Get("Song_YT_Error"), code), Loc.Get("Song_YT_Open"), Loc.Get("Common_Cancel"));
        if (open) await Launcher.Default.OpenAsync($"https://www.youtube.com/watch?v={_vm.Song.SourceRef}");
    }

    // ---- slider -------------------------------------------------------

    private void OnSeekerDragStarted(object? sender, EventArgs e) => BeginSliderScrub();

    private void OnSeekerChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_sliderFromCode) return;
        BeginSliderScrub();
        _vm.Scrubbing(e.NewValue);
        _scrubEnd.Stop();
        _scrubEnd.Start();
    }

    private void OnSeekerDragCompleted(object? sender, EventArgs e) => EndSliderScrub();

    private void BeginSliderScrub()
    {
        if (_sliderScrubbing) return;
        _sliderScrubbing = true;
        _ = _vm.ScrubStartAsync();
    }

    private void EndSliderScrub()
    {
        _scrubEnd.Stop();
        if (!_sliderScrubbing) return;
        _sliderScrubbing = false;
        _ = _vm.ScrubEndAsync(Seeker.Value);
    }

    /// <summary>
    /// Collapsed player: the chords sit at the edges with the arrow between them
    /// (space-between). Expanded: the row is shorter and the boxes smaller, so
    /// they read better spread evenly with air on the outside (space-around).
    /// </summary>
    private void ApplyPanelSpacing(bool around)
    {
        var edge = around ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var gap = new GridLength(around ? 2 : 1, GridUnitType.Star);
        Panel.ColumnDefinitions[0].Width = Panel.ColumnDefinitions[6].Width = edge;
        Panel.ColumnDefinitions[2].Width = Panel.ColumnDefinitions[4].Width = gap;
    }

    // ---- taps ---------------------------------------------------------

    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Navigation.PopAsync(animated: true);

    private async void OnEditorTapped(object? sender, TappedEventArgs e) =>
        await DisplayAlert(Loc.Get("Song_Editor"), Loc.Get("Song_Editor_Soon"), "OK");

    private Task OnShapeTappedAsync(string chord)
    {
        if (string.IsNullOrEmpty(chord) || chord == "—") return Task.CompletedTask;
        _ = _vm.PauseAsync();                                    // look at the neck without chasing the music
        _sheetOpen = true;
        var (positions, index) = _vm.ShapeChoices(chord);
        return ChordShapesSheet.ShowAsync(chord, positions, index, _vm.LeftHanded, i => _vm.ChooseShape(chord, i));
    }

    private async void OnCurrentShapeTapped(object? sender, TappedEventArgs e) => await OnShapeTappedAsync(_vm.CurrentChord);

    private async void OnNextShapeTapped(object? sender, TappedEventArgs e) => await OnShapeTappedAsync(_vm.NextChord);

    /// <summary>The YouTube strip expands the player (and back). The WebView
    /// stays alive while collapsed so playback continues. The player never takes
    /// more than half of what the chords and the conveyor have between them —
    /// otherwise the conveyor slides under the transport.</summary>
    private async void OnPlayerStripTapped(object? sender, TappedEventArgs e) => await SetPlayerExpandedAsync(!_vm.PlayerExpanded, animate: true);

    private async Task SetPlayerExpandedAsync(bool expanded, bool animate)
    {
        _vm.PlayerExpanded = expanded;
        PlayerChevron.Name = expanded ? "chevD" : "chevR";
        ApplyPanelSpacing(around: expanded);
        var m = Theme.Metrics.Instance;
        if (expanded)
        {
            // Before the first layout the rows have no height yet: fall back to the full size.
            double free = Panel.Height + Track.Height;
            PlayerHost.HeightRequest = free > 0 ? Math.Clamp(free * 0.45, m.Size(110), m.Size(200, hero: true)) : m.Size(200, hero: true);
            if (animate) await PlayerHost.FadeTo(1, 200); else PlayerHost.Opacity = 1;
        }
        else
        {
            if (animate) await PlayerHost.FadeTo(0, 150); else PlayerHost.Opacity = 0;
            PlayerHost.HeightRequest = 1;
        }
    }

    /// <summary>The "more" sheet slides up over the song: key, capo, speed,
    /// chord vocabulary, A–B and volume — everything not needed every bar.</summary>
    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        bool opening = MoreSheet.TranslationY > 1;
        MoreScrim.InputTransparent = !opening;
        if (opening)
        {
            _ = MoreScrim.FadeTo(0.45, 180);
            await MoreSheet.TranslateTo(0, 0, 260, Easing.CubicOut);
        }
        else
        {
            _ = MoreScrim.FadeTo(0, 160);
            await MoreSheet.TranslateTo(0, MoreSheet.Height + 40, 220, Easing.CubicIn);
        }
    }
}
