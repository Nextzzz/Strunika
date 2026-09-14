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
        // The gaps the XAML gives the track and the grid, for when the editor gives them back.
        _trackMargin = Track.Margin;
        _gridTop = GridHost.Margin.Top;
        _gridBottom = GridHost.Margin.Bottom;
        _vm = new SongViewModel(song, services.GetRequiredService<ISongRepository>(), services.GetRequiredService<IProGate>(), services.GetRequiredService<IClickPlayer>());
        BindingContext = _vm;
        _vm.ProRequired += (_, f) => { _sheetOpen = true; _ = PaywallSheet.ShowAsync(f); };
        _vm.Message += (_, text) => _ = this.DisplayAlertAsync(song.Title, text, "OK");

        Track.ScrubStarted += (_, _) => _ = _vm.ScrubStartAsync();
        Track.Scrubbing += (_, t) => _vm.Scrubbing(t);
        Track.ScrubEnded += (_, t) => _ = _vm.ScrubEndAsync(t);
        Track.SeekRequested += (_, t) => _ = _vm.SeekAsync(t);
        Track.SelectionRequested += (_, index) => _vm.Selected = index;
        Track.FollowingChanged += (_, _) => UpdateFollowChip();
        // The same map serves both views of the song: on the track it moves the
        // window, in the beat view it scrolls to the row (user decision 2026-09-10).
        Map.ViewRequested += (_, middle) =>
        {
            if (_gridView) ScrollGridToTime(middle);
            else Track.LookAt(middle);
        };
        Track.SegmentMoved += (_, at) => _ = _vm.MoveSegmentAsync(at.Index, at.Start);
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
            if (e.PropertyName == nameof(SongViewModel.SelectedChord)) Dispatcher.Dispatch(FitChordName);
        };
        ChordChip.SizeChanged += (_, _) => FitChordName();
        Theme.Refit.Watch(this, FitChordName);

        // Moving the song's own slider is asking to be where the song is: the
        // editor's track goes back to riding along with it (user request 2026-09-10).
        Seeker.DragStarted += (_, _) => { Track.FollowNow(); _ = _vm.ScrubStartAsync(); };
        Seeker.Dragging += (_, t) => _vm.Scrubbing(t);
        Seeker.DragCompleted += (_, t) => { Track.FollowNow(); _ = _vm.ScrubEndAsync(t); };
        // The sheet is put away by its own height. It used to be pushed down by a
        // flat 600 pt, and a sheet taller than that — the metronome's level row
        // appearing was enough — kept a strip of itself on screen (user report
        // 2026-09-10).
        // Before the first measurement the screen's own shortest side is the one
        // honest guess at "off the bottom"; after it, the sheet's own height is.
        MoreSheet.TranslationY = Theme.Metrics.Instance.ShortestSide;
        MoreSheet.SizeChanged += (_, _) => { if (!_moreOpen) MoreSheet.TranslationY = HiddenSheet; };

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
        BeatsView.BeatChosen += (_, beat) =>
        {
            // The empty square already chosen, tapped again: the song goes there,
            // as the chosen chord's square does (user request 2026-09-14).
            bool again = beat == _vm.ChosenBeat && !_vm.HasSelection;
            _vm.ChooseAtBeat(beat);
            if (again && _vm.ChosenBeat == beat && beat < _vm.BeatTimes.Length) _ = _vm.SeekAsync(_vm.BeatTimes[beat]);
            _gridFollowing = false;                              // the beat view is the reader's now
            UpdateFollowChip();
        };
        // The chosen chord tapped again: the song goes to it (user request 2026-09-14).
        BeatsView.ChosenTapped += (_, _) =>
        {
            if (_vm.Editing && _vm.HasSelection) _ = _vm.SeekAsync(_vm.SelectedStart);
        };
        GridHost.Scrolled += OnGridScrolled;
        BeatsView.ActiveMoved += OnActiveBeatMoved;
        // A chord dragged from one square onto another.
        BeatsView.ChordDropped += async (_, move) =>
        {
            var beats = _vm.BeatTimes;
            if (move.To < 0 || move.To >= beats.Length) return;
            await _vm.MoveSelectedToAsync(beats[move.To]);
            _vm.ChooseAtBeat(move.To);
        };
        ApplyViewMode(AppSettings.SongGridView && _vm.BeatTimes.Length > 0, save: false);

        // Leaving the tree: stop the ticker; release the player unless a sheet is
        // merely covering the page (it comes back through OnAppearing).
        // Back in the tree — from a sheet, above all. The ticker is started here
        // and not only in OnAppearing: on Windows a modal unloads the page, and
        // OnAppearing runs *before* Loaded on the way back, so the frames it
        // started were stopped again by the first tick, which still saw the page
        // as gone. The song froze until it was left and opened again (user
        // report 2026-09-10).
        Loaded += (_, _) =>
        {
            _unloaded = false;
            StartFrames();
#if IOS
            WatchDeviceVolume();                                 // a sheet over the page took the volume view away
#endif
        };
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
                if (_vm.Editing)
                {
                    // The chosen chord's square, or the empty square a new chord would go on.
                    BeatsView.Chosen = ChosenSquare;
                    ShowGridOnMap();
                }
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
        // Where the view going away was looking, and whether it rode along with
        // the song: the view coming in takes both, so the switch keeps the reader
        // where they were. The beat view used to open wherever it last scrolled
        // to — the top of the song, as often as not (user report 2026-09-14).
        bool switching = grid != _gridView;
        bool following = true;
        double lookAt = double.NaN;
        if (switching && _vm.Editing)
        {
            following = _gridView ? _gridFollowing : Track.Following;
            lookAt = _gridView ? GridCentreTime() : Track.CentreTime;
        }
        _gridView = grid;
        _vm.GridView = grid;
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
        if (switching) CarryView(grid, following, lookAt);
    }

    /// <summary>The view just shown looks where the other one did: riding along,
    /// it rides along too; let go of, it opens on the same moment in its middle.</summary>
    private void CarryView(bool toGrid, bool following, double lookAt)
    {
        if (toGrid)
        {
            _gridFollowing = following;
            UpdateFollowChip();
            AlignGrid(following || double.IsNaN(lookAt) ? _vm.Position : lookAt, following);
            return;
        }
        if (following || double.IsNaN(lookAt)) Track.FollowNow();
        else LookWhenSized(lookAt);
        UpdateFollowChip();
    }

    /// <summary>The track has a width to centre by once it has been laid out.</summary>
    private void LookWhenSized(double middle, int attempt = 0)
    {
        if (Track.Width > 0) { Track.LookAt(middle); return; }
        if (attempt < 20) Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), () => LookWhenSized(middle, attempt + 1));
    }

    /// <summary>The moment in the middle of the beat view — the middle of the map's window.</summary>
    private double GridCentreTime()
    {
        var beats = _vm.BeatTimes;
        double step = BeatsView.RowPitch;
        int columns = BeatsView.Columns;
        if (beats.Length == 0 || step <= 0 || columns <= 0 || GridHost.Height <= 0) return _vm.Position;
        return BeatMath.TimeAt(beats, (GridHost.ScrollY / step + GridHost.Height / step / 2) * columns);
    }

    /// <summary>The beat view looking at a moment. Riding along, the playing row
    /// is the second from the top, as it always is; let go of, the moment is in
    /// the middle of the screen. A grid only just shown has no rows yet, so this
    /// waits for its layout.</summary>
    private void AlignGrid(double time, bool following, int attempt = 0)
    {
        var beats = _vm.BeatTimes;
        if (beats.Length == 0 || !_gridView) return;
        double step = BeatsView.RowPitch;
        int columns = BeatsView.Columns;
        if (step <= 0 || columns <= 0 || GridHost.Height <= 0)
        {
            if (attempt < 20) Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), () => AlignGrid(time, following, attempt + 1));
            return;
        }
        double target;
        if (following)
        {
            int index = Array.BinarySearch(beats, time);
            if (index < 0) index = ~index - 1;
            target = (Math.Max(0, index) / columns - 1) * step;
        }
        else
        {
            target = (BeatMath.IndexAt(beats, time) / columns - GridHost.Height / step / 2) * step;
        }
        double most = Math.Max(0, GridHost.ContentSize.Height - GridHost.Height);
        _ = ScrollGridSelfAsync(Math.Clamp(target, 0, most), animated: false);
    }

    /// <summary>The square the grid outlines: the chosen chord's, or the empty
    /// square a new chord would go on — and none outside the editor.</summary>
    private int ChosenSquare => !_vm.Editing ? -1 : _vm.HasSelection ? _vm.SelectedBeat : _vm.ChosenBeat;

    // ---- the chord editor ------------------------------------------------

    /// <summary>
    /// The editor is a way of playing the song, not another page: the player,
    /// the transport and the chord steps stay exactly where they are. What it
    /// adds is a row of its own over the play button, and what it takes to pay
    /// for that row is the chord diagrams in the conveyor view — the beat view
    /// simply shows fewer squares. The YouTube player is folded away and stays
    /// folded: the song is being read, not watched (user decision 2026-09-09).
    /// </summary>
    private bool _editorShown, _playerExpandedBeforeEditor;
    private Thickness _trackMargin;
    private double _gridTop, _gridBottom, _gridTopApplied = double.NaN;

    /// <summary>The editor's panel folded down to its chord row — the song's level
    /// and the metronome's row hidden — as the reader left it for this song in
    /// this view (see AppSettings.EditorPanelFolded).</summary>
    private void ApplyPanelFold()
    {
        bool folded = _vm.Editing && AppSettings.EditorPanelFolded(_vm.Song.Id, _gridView);
        if (LevelRow.IsVisible == folded) LevelRow.IsVisible = MetronomeRow.IsVisible = !folded;
        FoldIcon.Name = folded ? "chevU" : "chevD";              // which way the rows will go
    }

    private void OnFoldTapped(object? sender, TappedEventArgs e)
    {
        if (!_vm.Editing) return;
        AppSettings.SetEditorPanelFolded(_vm.Song.Id, _gridView, !AppSettings.EditorPanelFolded(_vm.Song.Id, _gridView));
        Services.Haptics.Default.Selection();
        ApplyPanelFold();
    }

    private void ApplyEditor()
    {
        bool editing = _vm.Editing;
        Panel.IsVisible = !_gridView && !editing;
        // The chord diagrams give their row to the map of the song; in the beat
        // view there is no track to map, so the row goes altogether.
        MapRow.IsVisible = editing;
        Map.IsVisible = editing;                                 // the same strip over both views
        // Editing, the map takes the row over the song and the grid drops to the
        // row below it; otherwise the grid has both rows to itself.
        Grid.SetRow(GridHost, editing ? 4 : 3);
        Grid.SetRowSpan(GridHost, editing ? 1 : 2);
        BeatsView.Editing = editing;
        UpdateFollowChip();
        BeatsView.Chosen = ChosenSquare;                         // at once, not a frame later: a stale square showed on switching views
        _gridSelfScrollUntil = Environment.TickCount64 + 500;     // the rows move under the new layout, not under a finger
        Body.RowDefinitions[3].Height = editing ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        // Under the map's chips the same gap as over them: the track and the grid
        // started a good way further down (user request 2026-09-14). The track
        // keeps a little room over its pills of its own, so it gives that back.
        ApplyPanelFold();
        double gap = MapRow.Spacing;
        var trackMargin = editing
            ? new Thickness(_trackMargin.Left, Math.Max(0, gap - Controls.ChordTrack.TopSpace), _trackMargin.Right, _trackMargin.Bottom)
            : _trackMargin;
        if (Track.Margin != trackMargin) Track.Margin = trackMargin;
        double gridTop = editing ? gap : _gridTop;
        if (gridTop != _gridTopApplied)
        {
            _gridTopApplied = gridTop;
            // Bound again rather than assigned: the margin also carries a tablet's content inset.
            GridHost.SetBinding(View.MarginProperty, new Theme.ContentInsetExtension { Top = gridTop, Bottom = _gridBottom }.ProvideValue(null!));
        }
        PlayerChevron.IsVisible = !editing;
        // The editor folds the player away; leaving it brings the player back the
        // way it was before (user request 2026-09-14). Only the way in and the way
        // out count: switching views inside the editor calls this too.
        if (editing != _editorShown)
        {
            _editorShown = editing;
            if (editing)
            {
                _playerExpandedBeforeEditor = _vm.PlayerExpanded;
                if (_vm.PlayerExpanded) _ = SetPlayerExpandedAsync(false, animate: true);
            }
            else if (_playerExpandedBeforeEditor && !_vm.PlayerExpanded)
            {
                _ = SetPlayerExpandedAsync(true, animate: true);
            }
        }
        FitChordName();
#if IOS
        WatchDeviceVolume();
#endif
    }

    /// <summary>The chosen chord's name fills its chip and shrinks rather than
    /// being cut: squeezed by the step arrows, "Am" came out as "…" on an iPhone
    /// (user report 2026-09-14). Measured, never guessed; run again when the
    /// name, the chip or the size class changes.</summary>
    private void FitChordName()
    {
        if (ChordName.Handler == null || ChordChip.Width <= 0) return;
        double size = Theme.Metrics.Instance.Size(26, hero: true);
        double pencil = ChordPencil.Width > 0 ? ChordPencil.Width : ChordPencil.Size;
        double room = ChordChip.Width - ChordChip.Padding.HorizontalThickness - pencil - ChordNameRow.Spacing - 4;
        if (_vm.HasSelection && room > 0)
        {
            if (Math.Abs(ChordName.FontSize - size) > 0.1) ChordName.FontSize = size;
            double natural = ChordName.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
            if (natural > room) size = Math.Max(Theme.Metrics.Instance.Size(12), size * room / natural);
        }
        if (Math.Abs(ChordName.FontSize - size) > 0.1) ChordName.FontSize = size;
    }

#if IOS
    /// <summary>A YouTube song's level slider is the device's own volume.
    /// Whichever one is on screen — the "…" sheet's or the editor's — starts
    /// where the volume buttons left it and follows them while it shows (user
    /// request 2026-09-14); with neither showing, the buttons get the system's
    /// overlay back.</summary>
    private void WatchDeviceVolume()
    {
        if (!_vm.IsYouTube) return;
        if (!_unloaded && (_moreOpen || _vm.Editing))
        {
            Platforms.iOS.SystemVolume.Attach(v => _vm.Volume = v);
            _vm.Volume = Platforms.iOS.SystemVolume.Get();
        }
        else Platforms.iOS.SystemVolume.Detach();
    }
#endif

    /// <summary>In the editor the sheet's place goes to the switch between the
    /// two views of the song, one tap instead of three.</summary>
    private void OnViewSwitchTapped(object? sender, TappedEventArgs e)
    {
        if (!_gridView && _vm.BeatTimes.Length == 0) return;      // nothing to grid without beats
        ApplyViewMode(!_gridView, save: true);
    }

    /// <summary>The follow chip keeps its place in the editor and is never hidden:
    /// dimmed and deaf while the view already rides along, live when there is
    /// somewhere to go back to (user request 2026-09-14).</summary>
    private void UpdateFollowChip()
    {
        bool editing = _vm.Editing;
        bool canFollow = editing && (_gridView ? !_gridFollowing : !Track.Following);
        if (FollowChip.IsVisible != editing) FollowChip.IsVisible = editing;
        FollowChip.Opacity = canFollow ? 1 : 0.45;               // a little dimmed, still plainly the accent
        FollowChip.InputTransparent = !canFollow;
    }

    /// <summary>Back to the playhead, and along with it from here on. The beat
    /// view scrolls itself, so there it is a flag and the next beat brings the
    /// rows back.</summary>
    private void OnFollowTapped(object? sender, TappedEventArgs e)
    {
        if (_gridView) { _gridFollowing = true; UpdateFollowChip(); AlignGrid(_vm.Position, following: true); }
        else Track.FollowNow();
    }

    /// <summary>Back to the chord being worked on, wherever the track has wandered.</summary>
    private void OnGoToSelectionTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedStart < 0) return;
        if (_gridView) { _gridFollowing = false; UpdateFollowChip(); ScrollGridToRow(_vm.SelectedBeat); }
        else Track.LookAt(_vm.SelectedStart);
    }

    private async void OnEditNudgeBack(object? sender, TappedEventArgs e) => await _vm.NudgeSelectedAsync(-1);

    private async void OnEditNudgeOn(object? sender, TappedEventArgs e) => await _vm.NudgeSelectedAsync(1);

    private async void OnEditDelete(object? sender, TappedEventArgs e)
    {
        if (!_vm.HasSelection) return;
        await _vm.DeleteSelectedAsync();
    }

    /// <summary>A chord from this moment on, taking the rest of the one it lands in.</summary>
    private async void OnEditAdd(object? sender, TappedEventArgs e)
    {
        _sheetOpen = true;
        if (!_vm.CanAdd) return;
        // On the track the cursor says where; in the beat view the beat chosen does.
        double at = _gridView ? _vm.ChosenBeatTime : Track.CentreTime;
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
    /// <summary>The beat view rides along with the song like the track does, and
    /// lets go the moment the reader chooses a beat in it (user request
    /// 2026-09-10). The chip over it brings it back.</summary>
    private bool _gridFollowing = true;
    private (int Row, double Step) _gridAt;

    private void ScrollGridToBeat() => ScrollGridToRow(_vm.SelectedBeat, own: false);

    /// <summary>Where the song is on the map, and how much of it the rows on
    /// screen cover — the same window the track draws, measured off the scroll.</summary>
    private void ShowGridOnMap()
    {
        var beats = _vm.BeatTimes;
        double step = BeatsView.RowPitch;
        int columns = BeatsView.Columns;
        if (beats.Length == 0 || step <= 0 || columns <= 0) return;
        // The rows on screen as they are, parts of rows included: row r begins at
        // beat r × columns, and a part of a row is that part of its beats. Whole
        // rows put a square in the middle of the screen off the middle of the
        // window (user report 2026-09-14).
        double top = GridHost.ScrollY / step, visible = GridHost.Height / step;
        double start = BeatMath.TimeAt(beats, top * columns);
        double end = BeatMath.TimeAt(beats, (top + visible) * columns);
        Map.Show(_vm.Position, start, Math.Max(0.01, end - start));
        Map.Mark(_vm.SelectedStart);
    }

    /// <summary>The map was dragged while the beat view is on: scroll to the row
    /// that holds that moment, and let the song go on without us.</summary>
    private void ScrollGridToTime(double seconds)
    {
        var beats = _vm.BeatTimes;
        if (beats.Length == 0 || BeatsView.Columns <= 0) return;
        int index = 0;
        while (index + 1 < beats.Length && beats[index + 1] <= seconds) index++;
        _gridFollowing = false;
        UpdateFollowChip();
        // Not animated: the window is dragged, and an animation started on every
        // move of the finger would queue up and lag a row behind it.
        ScrollGridToRow(index, animated: false);
    }

    private async void ScrollGridToRow(int beat, bool own = true, bool animated = true)
    {
        double step = BeatsView.RowPitch > 0 ? BeatsView.RowPitch : _gridAt.Step;
        if (step <= 0) return;
        int row = own && beat >= 0 && BeatsView.Columns > 0 ? beat / BeatsView.Columns : _gridAt.Row;
        double target = Math.Max(0, (row - 1) * step);
        await ScrollGridSelfAsync(target, animated);
    }

    /// <summary>Until this moment a scroll of the beat view is the page's own
    /// doing — an animation it started, or rows moving under a new layout — and
    /// not the reader's.</summary>
    private long _gridSelfScrollUntil;

    private async Task ScrollGridSelfAsync(double target, bool animated)
    {
        _gridSelfScrollUntil = Environment.TickCount64 + 800;
        try { await GridHost.ScrollToAsync(0, target, animated); }
        catch (Exception) { }                                    // torn down mid-scroll
        _gridSelfScrollUntil = Environment.TickCount64 + 150;    // its last events arrive after it
    }

    /// <summary>Scrolling the beat view by hand while it rides along lets go of
    /// the playhead, as panning the track does (user request 2026-09-14).</summary>
    private void OnGridScrolled(object? sender, ScrolledEventArgs e)
    {
        if (!_vm.Editing || !_gridView || !_gridFollowing) return;
        if (Environment.TickCount64 < _gridSelfScrollUntil) return;
        _gridFollowing = false;
        UpdateFollowChip();
    }

    private async void OnActiveBeatMoved(object? sender, (int Row, double Step) at)
    {
        _gridAt = at;
        if (!_gridView || _unloaded || at.Step <= 0) return;
        if (_vm.Editing && !_gridFollowing) return;              // the reader is working in it
        // The row being played is the second from the top: one row of history
        // above it, everything that is coming below. At the start of the song
        // there is nothing above, so it simply stays at the top.
        double target = Math.Max(0, (at.Row - 1) * at.Step);
        if (Math.Abs(GridHost.ScrollY - target) < 1) return;
        await ScrollGridSelfAsync(target, animated: true);
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

    /// <summary>Far enough down to be gone: the sheet's own height and a finger's
    /// worth over it, so a shadow does not peek either.</summary>
    private double HiddenSheet => MoreSheet.Height + Theme.Metrics.Instance.Size(40);

    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        bool opening = !_moreOpen;
        _moreOpen = opening;
        MoreScrim.InputTransparent = !opening;
        if (opening)
        {
#if IOS
            WatchDeviceVolume();                                 // the sheet's slider is the device volume for a YouTube song
#endif
            _ = MoreScrim.FadeToAsync(0.45, 180);
            await MoreSheet.TranslateToAsync(0, 0, 260, Easing.CubicOut);
        }
        else
        {
#if IOS
            WatchDeviceVolume();                                 // the editor's slider may still want the buttons
#endif
            _ = MoreScrim.FadeToAsync(0, 160);
            await MoreSheet.TranslateToAsync(0, HiddenSheet, 220, Easing.CubicIn);
        }
    }
}
