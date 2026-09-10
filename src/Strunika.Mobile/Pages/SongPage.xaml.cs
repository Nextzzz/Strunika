using Strunika.Mobile.Theme;
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
    private readonly Stopwatch _clock = new();
    /// <summary>A sheet is covering the page: it disappears, but it must keep
    /// its player — otherwise coming back reloads the song from the start.</summary>
    private bool _sheetOpen, _attached, _unloaded, _windowHooked;
    private Window? _hookedWindow;

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        _unloaded = true;
        StopFrames();
        _vm.Dispose();
    }
    private int _frame;

    public SongPage(Song song, IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _vm = new SongViewModel(song, services.GetRequiredService<ISongRepository>(), services.GetRequiredService<IProGate>(), services.GetRequiredService<IClickPlayer>());
        BindingContext = _vm;
        _vm.ProRequired += (_, f) => { _sheetOpen = true; _ = PaywallSheet.ShowAsync(f); };
        _vm.Message += (_, text) => _ = this.DisplayAlertAsync(song.Title, text, "OK");

        Track.ScrubStarted += (_, _) => _ = _vm.ScrubStartAsync();
        Track.Scrubbing += (_, t) => _vm.Scrubbing(t);
        Track.ScrubEnded += (_, t) => _ = _vm.ScrubEndAsync(t);
        Track.SeekRequested += (_, t) => _ = _vm.SeekAsync(t);
        Track.SelectionRequested += (_, index) => _vm.Selected = index;
        Track.FollowingChanged += (_, following) => FollowChip.IsVisible = _vm.Editing && !following;
        Map.ViewRequested += (_, middle) => Track.LookAt(middle);
        Track.SegmentMoved += (_, at) => _ = _vm.SetSegmentAsync(at.Index, at.Start, at.End);
        Track.LoopEditStarted += (_, _) => _ = _vm.LoopEditStartAsync();
        Track.LoopChanging += (_, loop) => _vm.LoopEditMoved(loop.Start, loop.End);
        Track.LoopEditEnded += (_, at) => _ = _vm.LoopEditEndAsync(at);
        // While the start is taken and the end is not, the chip breathes: with
        // the band growing on the conveyor it leaves no doubt the loop is being
        // taken (user request 2026-09-09).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SongViewModel.LoopArmed)) BreatheLoopChip();
            if (e.PropertyName == nameof(SongViewModel.Editing)) ApplyEditor();
        };

        // Moving the song's own slider is asking to be where the song is: the
        // editor's track goes back to riding along with it (user request 2026-09-10).
        Seeker.DragStarted += (_, _) => { Track.FollowNow(); _ = _vm.ScrubStartAsync(); };
        Seeker.Dragging += (_, t) => _vm.Scrubbing(t);
        Seeker.DragCompleted += (_, t) => { Track.FollowNow(); _ = _vm.ScrubEndAsync(t); };
        // The sheet is put away by its own height. It used to be pushed down by a
        // flat 600 pt, and a sheet taller than that — the metronome's level row
        // appearing was enough — kept a strip of itself on screen (user report
        // 2026-09-10).
        MoreSheet.SizeChanged += (_, _) => { if (!_moreOpen) MoreSheet.TranslationY = MoreSheet.Height + 40; };

        ApplyPanelSpacing(around: false);
#if IOS
        Platforms.iOS.AudioSessions.ForPlayback();               // before the video starts, never during it
        // Edge to edge at the bottom (MAUI 10 pads layouts by the safe area on
        // its own): the transport sits 8 pt above the home indicator instead of
        // 20 pt above the safe area, and the sheet and its scrim reach the
        // screen's edge. The top keeps the status-bar inset.
        Root.SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.None);
        Body.SafeAreaEdges = SafeAreaEdges.None;
        Transport.SafeAreaEdges = SafeAreaEdges.None;             // it dips into the inset: no padding of its own
        Theme.SafeArea.IgnoreBelow(MoreSheet);                    // the sheet pads for the indicator itself (below)
        // The play button's bottom lands 40 pt off the screen's edge: 19 pt clear
        // of the home indicator's 21 pt zone. (The page's legacy UseSafeArea is
        // gone from the XAML; it padded everything and hid this.)
        Body.Padding = new Thickness(0, 8, 0, Math.Max(0, Theme.SafeArea.Bottom - 10));
        MoreSheet.Padding = new Thickness(20, 10, 20, 24 + Theme.SafeArea.Bottom);
#endif

        BeatsView.SeekRequested += (_, t) => _ = _vm.SeekAsync(t);
        BeatsView.BeatChosen += (_, beat) => _vm.ChooseAtBeat(beat);
        BeatsView.ActiveMoved += OnActiveBeatMoved;
        ApplyViewMode(AppSettings.SongGridView && _vm.BeatTimes.Length > 0, save: false);

        // Leaving the tree: stop the ticker; release the player unless a sheet is
        // merely covering the page (it comes back through OnAppearing).
        // Back in the tree — from a sheet, above all. The ticker is started here
        // and not only in OnAppearing: on Windows a modal unloads the page, and
        // OnAppearing runs *before* Loaded on the way back, so the frames it
        // started were stopped again by the first tick, which still saw the page
        // as gone. The song froze until it was left and opened again (user
        // report 2026-09-10).
        Loaded += (_, _) => { _unloaded = false; StartFrames(); };
        Unloaded += (_, _) =>
        {
            _unloaded = true;
            StopFrames();
#if IOS
            Platforms.iOS.SystemVolume.Detach();
#endif
            if (_sheetOpen) return;                                  // a sheet is merely covering the page
            _vm.Dispose();
            if (_hookedWindow != null) { _hookedWindow.Destroying -= OnWindowDestroying; _hookedWindow = null; _windowHooked = false; }
        };

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
        if (!_windowHooked && Window != null)
        {
            // Closing the window does not raise OnDisappearing/Unloaded early enough
            // for the ticker: stop it here, before the native tree is torn down.
            // Unhooked on Unloaded — the Window outlives every song page, and a
            // lingering subscription would keep each page (and its canvases) alive.
            _windowHooked = true;
            _hookedWindow = Window;
            Window.Destroying += OnWindowDestroying;
        }
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
            await this.DisplayAlertAsync(_vm.Title, Loc.Get("Library_Err_File"), "OK");
        }
        _attached = true;
        _clock.Restart();
        StartFrames();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.AbortAnimation(FramesHandle);
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

    // A 16 ms animation restarted on every tick fired twice per vsync (120 calls
    // a second on the dev head); a long one just rides the ticker.
    private void StartFrames()
    {
        this.AbortAnimation(FramesHandle);                       // never two tickers
        new Animation(_ => OnFrame()).Commit(this, FramesHandle, length: 3_600_000, repeat: () => true);
    }

    private void StopFrames() => this.AbortAnimation(FramesHandle);

    // ---- frame diagnostics: every hitch over 50 ms is logged with what coincided ----
    private int _gc0Seen, _frames, _hitches, _lastSecond = -1;
    private double _worst, _sumDt, _sinceReport;
    private bool _secondTickedLastFrame;
    private long _allocatedSeen = -1;
    /// <summary>Diagnostics: leave the conveyor still while the song plays, to tell
    /// drawing from audio as the source of a stall (Settings → About, debug).</summary>


    private void OnFrame()
    {
        // Closing the window skips OnDisappearing: once the page has left the
        // tree the ticker must not touch views whose native side is gone.
        // (Never test Handler here: on Windows the first frames run before it exists.)
        if (_unloaded) { StopFrames(); return; }
        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
#if DEBUG
        int gc0 = GC.CollectionCount(0);
        bool collected = gc0 != _gc0Seen;
        _gc0Seen = gc0;
        _frames++;
        _sumDt += dt;
        _sinceReport += dt;
        if (dt > _worst) _worst = dt;
        if (dt > 0.05 && _frames > 5)
        {
            _hitches++;
            var gc = GC.GetGCMemoryInfo(GCKind.Any);
            double pause = 0;
            foreach (var p in gc.PauseDurations) pause += p.TotalMilliseconds;
            FileLog.Info($"frame hitch {dt * 1000:0} ms at {_vm.Position:0.00} s [{_vm.TransportKind}] gc {collected} last-gc gen{gc.Generation} pause {pause:0} ms compacted {gc.Compacted} concurrent {gc.Concurrent} promoted {gc.PromotedBytes / 1024} KB heap {gc.HeapSizeBytes / 1048576} MB loh {gc.GenerationInfo[3].SizeAfterBytes / 1048576} MB second-tick {_secondTickedLastFrame} probing {_vm.IsProbing} playing {_vm.IsPlaying}");
        }
        if (_sinceReport >= 5)
        {
            long allocated = GC.GetTotalAllocatedBytes(false);
            if (_allocatedSeen < 0) _allocatedSeen = allocated;
            FileLog.Info($"frames 5 s: {_frames} frames, avg {1000 * _sumDt / Math.Max(1, _frames):0.0} ms, worst {_worst * 1000:0} ms, hitches {_hitches}, gc0 {gc0}, gc1 {GC.CollectionCount(1)}, gc2 {GC.CollectionCount(2)}, allocated {(allocated - _allocatedSeen) / 1024} KB, managed {GC.GetTotalMemory(false) / 1048576} MB");
            _allocatedSeen = allocated;
            _sinceReport = 0; _frames = 0; _sumDt = 0; _worst = 0; _hitches = 0;
        }
#endif
        try
        {
            _vm.Frame(Math.Min(dt, 0.25));                       // a stalled frame must not jump the song
            int second = (int)_vm.Position;
            _secondTickedLastFrame = second != _lastSecond;
            _lastSecond = second;
            // The conveyor and the seek bar are driven directly, not through
            // bindings, and both move by transforms: no drawing, no native
            // control, no layout in the frame.
            if (Math.Abs(Track.Position - _vm.Position) > 0.002) Track.Position = _vm.Position;
            if (_gridView)
            {
                BeatsView.Position = _vm.Position;               // a comparison unless the beat changed
                if (_vm.Editing) BeatsView.Chosen = _vm.SelectedBeat;
            }
            else if (_vm.Editing)
            {
                Map.Show(_vm.Position, Track.ViewStart, Track.ViewSpan);
                Map.Mark(_vm.SelectedStart);
            }
            if (++_frame % 3 == 0)
            {
                Seeker.Position = _vm.Position;
            }
        }
        catch (Exception ex) { FileLog.Error("song frame", ex); }
    }

    /// <summary>101/150 = the owner disallows embedding, 153 = the page had no
    /// usable origin. Either way the only honest offer is YouTube itself.</summary>
    private async void OnPlayerError(object? sender, int code)
    {
        var open = await this.DisplayAlertAsync(_vm.Title, string.Format(Loc.Get("Song_YT_Error"), code), Loc.Get("Song_YT_Open"), Loc.Get("Common_Cancel"));
        if (open) await Launcher.Default.OpenAsync($"https://www.youtube.com/watch?v={_vm.Song.SourceRef}");
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

    // ---- conveyor | beat grid ------------------------------------------

    private bool _gridView;

    private void OnConveyorModeTapped(object? sender, TappedEventArgs e) => ApplyViewMode(false, save: true);

    private void OnGridModeTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.BeatTimes.Length == 0) return;                   // nothing to grid without beats
        ApplyViewMode(true, save: true);
    }

    /// <summary>One of two views of the same rows: the moving conveyor, or the
    /// song laid out as beats. The transport, slider and player stay put.</summary>
    private void ApplyViewMode(bool grid, bool save)
    {
        _gridView = grid;
        if (save) AppSettings.SongGridView = grid;
        Track.IsVisible = !grid;
        GridHost.IsVisible = grid;
        BeatsView.SetOnScreen(grid);                             // hidden, it draws nothing and remembers instead
        GridShade.IsVisible = grid;
        if (grid) ApplyGridShade();
        ModeRow.IsVisible = _vm.BeatTimes.Length > 0;            // nothing to grid without beats
        var on = Theme.Tokens.Current("Fill");
        var onIcon = Theme.Tokens.Current("OnFill");
        var off = Theme.Tokens.Current("TextSec");
        ConveyorSeg.BackgroundColor = grid ? Colors.Transparent : on;
        GridSeg.BackgroundColor = grid ? on : Colors.Transparent;
        ConveyorIcon.Color = grid ? off : onIcon;
        GridIcon.Color = grid ? onIcon : off;
        ViewSwitchIcon.Name = grid ? "conveyor" : "grid4";        // what the tap will bring, not what is here
        if (grid) BeatsView.Position = _vm.Position;
        ApplyEditor();
    }

    // ---- the chord editor ------------------------------------------------

    /// <summary>
    /// The editor is a way of playing the song, not another page: the player,
    /// the transport and the chord steps stay exactly where they are. What it
    /// adds is a row of its own over the play button, and what it takes to pay
    /// for that row is the chord diagrams in the conveyor view — the beat view
    /// simply shows fewer squares. The YouTube player is folded away and stays
    /// folded: the song is being read, not watched (user decision 2026-09-09).
    /// </summary>
    private void ApplyEditor()
    {
        bool editing = _vm.Editing;
        Panel.IsVisible = !_gridView && !editing;
        // The chord diagrams give their row to the map of the song; in the beat
        // view there is no track to map, so the row goes altogether.
        MapRow.IsVisible = editing && !_gridView;
        BeatsView.Editing = editing;
        FollowChip.IsVisible = editing && !_gridView && !Track.Following;
        Body.RowDefinitions[3].Height = editing
            ? (_gridView ? new GridLength(0) : GridLength.Auto)
            : new GridLength(3, GridUnitType.Star);
        PlayerChevron.IsVisible = !editing;
        if (editing && _vm.PlayerExpanded) _ = SetPlayerExpandedAsync(false, animate: true);
    }

    /// <summary>In the editor the sheet's place goes to the switch between the
    /// two views of the song, one tap instead of three.</summary>
    private void OnViewSwitchTapped(object? sender, TappedEventArgs e)
    {
        if (!_gridView && _vm.BeatTimes.Length == 0) return;      // nothing to grid without beats
        ApplyViewMode(!_gridView, save: true);
    }

    /// <summary>Back to the playhead, and along with it from here on.</summary>
    private void OnFollowTapped(object? sender, TappedEventArgs e) => Track.FollowNow();

    /// <summary>Back to the chord being worked on, wherever the track has wandered.</summary>
    private void OnGoToSelectionTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedStart >= 0) Track.LookAt(_vm.SelectedStart);
    }

    private async void OnEditDelete(object? sender, TappedEventArgs e)
    {
        if (!_vm.HasSelection) return;
        await _vm.DeleteSelectedAsync();
    }

    /// <summary>A chord from this moment on, taking the rest of the one it lands in.</summary>
    private async void OnEditAdd(object? sender, TappedEventArgs e)
    {
        _sheetOpen = true;
        double at = Track.CentreTime;                            // where the cursor stands on the track
        await ChordPickerSheet.ShowAsync(_vm.SelectedChord, offerAll: false, (label, _) => _vm.AddChordAsync(label, at));
    }

    /// <summary>Another chord in place of the one under the playhead.</summary>
    private async void OnEditChord(object? sender, TappedEventArgs e)
    {
        if (!_vm.HasSelection) return;
        _sheetOpen = true;
        await ChordPickerSheet.ShowAsync(_vm.SelectedChord, offerAll: true, (label, all) => _vm.SetSelectedAsync(label, all));
    }

    /// <summary>The fade over the last rows: the page background at alpha 0
    /// rising to opaque, so the grid slips under the player rather than being
    /// sliced by it.</summary>
    private void ApplyGridShade()
    {
        var bg = Theme.Tokens.Current("Bg");
        GridShade.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(bg.WithAlpha(0f), 0f),
                new GradientStop(bg.WithAlpha(0.75f), 0.55f),
                new GradientStop(bg, 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }

    /// <summary>
    /// Follow the song by whole rows: the offset is always a multiple of the row
    /// pitch, so the top row is never half cut — and a row the reader scrolled to
    /// crookedly is squared up again the moment the song moves on.
    /// </summary>
    private async void OnActiveBeatMoved(object? sender, (int Row, double Step) at)
    {
        if (!_gridView || _unloaded || at.Step <= 0) return;
        // The row being played is the second from the top: one row of history
        // above it, everything that is coming below. At the start of the song
        // there is nothing above, so it simply stays at the top.
        double target = Math.Max(0, (at.Row - 1) * at.Step);
        if (Math.Abs(GridHost.ScrollY - target) < 1) return;
        try { await GridHost.ScrollToAsync(0, target, animated: true); }
        catch (Exception) { }                                    // torn down mid-scroll
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Navigation.PopAsync(animated: true);

    private async void OnTitleTapped(object? sender, TappedEventArgs e)
    {
        _sheetOpen = true;
        await SongInfoSheet.ShowAsync(_vm);
    }

    private Task OnShapeTappedAsync(string chord)
    {
        if (string.IsNullOrEmpty(chord) || chord == "—") return Task.CompletedTask;
        _ = _vm.PauseAsync();                                    // look at the neck without chasing the music
        _sheetOpen = true;
        var (positions, index) = _vm.ShapeChoices(chord);
        return ChordShapesSheet.ShowAsync(chord, positions, index, _vm.LeftHanded, _vm.Capo, i => _vm.ChooseShape(chord, i));
    }

    private async void OnCurrentShapeTapped(object? sender, TappedEventArgs e) => await OnShapeTappedAsync(_vm.CurrentChord);

    private async void OnNextShapeTapped(object? sender, TappedEventArgs e) => await OnShapeTappedAsync(_vm.NextChord);

    /// <summary>The YouTube strip expands the player (and back). The WebView
    /// stays alive while collapsed so playback continues. The player never takes
    /// more than half of what the chords and the conveyor have between them —
    /// otherwise the conveyor slides under the transport.</summary>
    private async void OnPlayerStripTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.Editing) return;                                 // folded away for the editor's sake
        await SetPlayerExpandedAsync(!_vm.PlayerExpanded, animate: true);
    }

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
            if (animate) await PlayerHost.FadeToAsync(1, 200); else PlayerHost.Opacity = 1;
        }
        else
        {
            if (animate) await PlayerHost.FadeToAsync(0, 150); else PlayerHost.Opacity = 0;
            PlayerHost.HeightRequest = 1;
        }
    }

    private bool _breathing;

    private async void BreatheLoopChip()
    {
        if (!_vm.LoopArmed) { _breathing = false; LoopChip.Opacity = 1; return; }
        if (_breathing || Motion.Reduced) return;
        _breathing = true;
        while (_breathing && !_unloaded && _vm.LoopArmed)
        {
            await LoopChip.FadeToAsync(0.45, 480, Easing.SinInOut);
            await LoopChip.FadeToAsync(1, 480, Easing.SinInOut);
        }
        _breathing = false;
        LoopChip.Opacity = 1;
    }

    /// <summary>The "more" sheet slides up over the song: key, capo, speed,
    /// chord vocabulary, A–B and volume — everything not needed every bar.</summary>
    private bool _moreOpen;

    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        bool opening = !_moreOpen;
        _moreOpen = opening;
        MoreScrim.InputTransparent = !opening;
        if (opening)
        {
#if IOS
            // A YouTube song's slider is the device volume: while it shows, the
            // buttons move it (and the system's overlay stays away); the moment
            // the sheet closes the overlay is the system's again.
            if (_vm.IsYouTube)
            {
                Platforms.iOS.SystemVolume.Attach(v => _vm.Volume = v);
                _vm.Volume = Platforms.iOS.SystemVolume.Get();     // where the buttons left it since the song opened
            }
#endif
            _ = MoreScrim.FadeToAsync(0.45, 180);
            await MoreSheet.TranslateToAsync(0, 0, 260, Easing.CubicOut);
        }
        else
        {
#if IOS
            Platforms.iOS.SystemVolume.Detach();
#endif
            _ = MoreScrim.FadeToAsync(0, 160);
            await MoreSheet.TranslateToAsync(0, MoreSheet.Height + 40, 220, Easing.CubicIn);
        }
    }
}
