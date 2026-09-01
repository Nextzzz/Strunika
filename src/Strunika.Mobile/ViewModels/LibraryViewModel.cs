using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strunika.Core.Diagnostics;
using Strunika.Core.Library;
using Strunika.Mobile.Data;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

/// <summary>
/// The Songs tab: library list with search / filter / sort, the three ways
/// of adding a song (file, recording, YouTube), analysis progress per card
/// and the free quota. Songs are inserted first (so the card appears at
/// once) and analysed by <see cref="AnalysisService"/> in the background.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    public const int FilterAll = 0, FilterFavourites = 1, FilterFolders = 2;

    private readonly ISongRepository _songs;
    private readonly AnalysisService _analysis;
    private readonly FreeQuota _quota;
    private readonly IYouTubeSource _youtube;
    private readonly IProGate _pro;
    private readonly List<SongItem> _all = new();

    /// <summary>Pro users see " Pro" + the wave after the page title.</summary>
    public bool IsPro => _pro.IsPro;

    public LibraryViewModel(ISongRepository songs, AnalysisService analysis, FreeQuota quota, IYouTubeSource youtube, IProGate pro)
    {
        _songs = songs;
        _analysis = analysis;
        _quota = quota;
        _youtube = youtube;
        _pro = pro;
        _analysis.Progress += OnProgress;
        _analysis.Finished += OnFinished;
        _pro.Changed += (_, _) => { OnPropertyChanged(nameof(FoldersLocked)); OnPropertyChanged(nameof(IsPro)); };
        Loc.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var item in _all) item.Refresh();
            OnPropertyChanged(nameof(SortLabel));
        };
    }

    public ObservableCollection<SongItem> Items { get; } = new();

    /// <summary>Songs per page. Filters, search and sorting run over the whole
    /// library first; only the slice on show is paged.</summary>
    public const int PageSize = 10;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _filter = FilterAll;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasSongs;
    [ObservableProperty] private bool _loaded;

    public bool FoldersLocked => !_pro.Has(Feature.Folders);

    // ---- pinned ways of adding (quick buttons at the top) --------------

    public bool PinYouTube => Pinned.Contains("youtube");
    public bool PinFile => Pinned.Contains("file");
    public bool PinRecord => Pinned.Contains("record");
    public bool AnyPinned => Pinned.Count > 0;

    private static HashSet<string> Pinned =>
        AppSettings.PinnedSources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    [RelayCommand]
    private void TogglePin(string source)
    {
        var set = Pinned;
        if (!set.Remove(source)) set.Add(source);
        AppSettings.PinnedSources = string.Join(",", set);
        Haptics.Default.Selection();
        OnPropertyChanged(nameof(PinYouTube));
        OnPropertyChanged(nameof(PinFile));
        OnPropertyChanged(nameof(PinRecord));
        OnPropertyChanged(nameof(AnyPinned));
    }

    /// <summary>The clipboard holds a YouTube link: ask whether to use it or browse.</summary>
    public event EventHandler<string>? YouTubeChoiceRequested;

    /// <summary>Open the built-in YouTube browser.</summary>
    public event EventHandler? BrowseYouTubeRequested;

    /// <summary>Open the recording sheet.</summary>
    public event EventHandler? RecordRequested;

    /// <summary>The YouTube button (quick row and add sheet): a link in the
    /// clipboard offers a choice, otherwise the built-in YouTube opens.</summary>
    public async Task YouTubeTapAsync()
    {
        if (!RemoteFlags.YouTubeAnalysis) { Message?.Invoke(this, Loc.Get("Library_YT_Off")); return; }
        var link = await ClipboardLinkAsync();
        if (link != null) YouTubeChoiceRequested?.Invoke(this, link);
        else BrowseYouTubeRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<string?> ClipboardLinkAsync()
    {
        try
        {
            if (!Clipboard.Default.HasText) return null;
            var text = (await Clipboard.Default.GetTextAsync())?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            bool url = text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
            return url && _youtube.TryParseVideoId(text) != null ? text : null;
        }
        catch (Exception ex)
        {
            FileLog.Error("clipboard", ex);
            return null;
        }
    }

    [RelayCommand]
    private Task QuickYouTube() => YouTubeTapAsync();

    [RelayCommand]
    private Task QuickFile() => AddFileAsync();

    [RelayCommand]
    private void QuickRecord() => RecordRequested?.Invoke(this, EventArgs.Empty);

    public string SortLabel => Loc.Get("Library_Sort_" + Capitalise(AppSettings.LibrarySort));

    /// <summary>A locked feature was tapped (folders, song limit).</summary>
    public event EventHandler<Feature>? ProRequired;

    /// <summary>The user wants a song opened (song page — M3).</summary>
    public event EventHandler<SongItem>? OpenRequested;

    /// <summary>Something to tell the user (localised text).</summary>
    public event EventHandler<string>? Message;

    partial void OnQueryChanged(string value) { Page = 0; Rebuild(); }

    partial void OnFilterChanged(int value) { Page = 0; Rebuild(); }

    /// <summary>
    /// The first page, fetched while the launch screen is still up, so opening
    /// the Songs tab paints straight away. The full list follows in
    /// <see cref="LoadAsync"/>; whatever it finds replaces this.
    /// </summary>
    public async Task PreloadAsync()
    {
        if (Loaded || _all.Count > 0) return;
        try
        {
            var songs = await _songs.GetRecentAsync(PageSize);
            if (Loaded || _all.Count > 0) return;                 // the full load won the race
            foreach (var song in songs) _all.Add(new SongItem(song));
            Rebuild();
        }
        catch (Exception ex)
        {
            FileLog.Error("library preload", ex);                // the full load will try again
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var songs = await _songs.GetAllAsync();
            _all.Clear();
            foreach (var song in songs)
            {
                // Jobs do not survive a restart: anything left "analysing" is retried on demand.
                if (song.Status == SongStatus.Analyzing || (song.Status == SongStatus.Pending && song.Error == null))
                {
                    song.Status = SongStatus.Failed;
                    song.Error = "Interrupted";
                    await _songs.UpdateAsync(song);
                }
                _all.Add(new SongItem(song));
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("library load", ex);
        }
        Loaded = true;
        Rebuild();
    }

    // ---- list shaping -------------------------------------------------

    [RelayCommand]
    private void SetFilter(string value)
    {
        int f = int.Parse(value);
        if (f == FilterFolders && FoldersLocked)
        {
            ProRequired?.Invoke(this, Feature.Folders);
            return;
        }
        Filter = f;
    }

    public void SetSort(string mode)
    {
        AppSettings.LibrarySort = mode;
        Page = 0;
        OnPropertyChanged(nameof(SortLabel));
        Rebuild();
    }

    /// <summary>Every song that passes the filters, in order — the page is a window on this.</summary>
    private List<SongItem> _matching = new();

    [ObservableProperty] private int _page;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private bool _hasPages;

    public string PageLabel => string.Format(Loc.Get("Library_Page"), Page + 1, PageCount);
    public bool CanPagePrevious => Page > 0;
    public bool CanPageNext => Page + 1 < PageCount;

    [RelayCommand]
    private void PreviousPage() { if (CanPagePrevious) { Page--; ApplyPage(); PageChanged?.Invoke(this, EventArgs.Empty); } }

    [RelayCommand]
    private void NextPage() { if (CanPageNext) { Page++; ApplyPage(); PageChanged?.Invoke(this, EventArgs.Empty); } }

    private void ApplyPage()
    {
        var page = _matching.Skip(Page * PageSize).Take(PageSize).ToList();
        // Cheap diff: rebuild only when the page actually differs.
        if (!page.SequenceEqual(Items))
        {
            Items.Clear();
            foreach (var item in page) Items.Add(item);
        }
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanPagePrevious));
        OnPropertyChanged(nameof(CanPageNext));
    }

    /// <summary>Raised only when the reader turns a page, so the view scrolls
    /// back to the top; a rebuild behind their back never moves the list.</summary>
    public event EventHandler? PageChanged;

    private void Rebuild()
    {
        IEnumerable<SongItem> list = _all;
        if (Filter == FilterFavourites) list = list.Where(i => i.Song.Favourite);
        if (Filter == FilterFolders) list = list.Where(i => i.Song.FolderId != null);
        var q = Query.Trim();
        if (q.Length > 0)
            list = list.Where(i => i.Song.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || i.Song.Artist.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        list = AppSettings.LibrarySort switch
        {
            "title" => list.OrderBy(i => i.Song.Title, StringComparer.CurrentCultureIgnoreCase),
            "key" => list.OrderBy(i => string.IsNullOrEmpty(i.Song.Key)).ThenBy(i => i.Song.Key).ThenByDescending(i => i.Song.CreatedAt),
            _ => list.OrderByDescending(i => i.Song.CreatedAt),
        };
        _matching = list.ToList();
        PageCount = Math.Max(1, (_matching.Count + PageSize - 1) / PageSize);
        HasPages = PageCount > 1;
        if (Page >= PageCount) Page = PageCount - 1;             // a filter can shorten the list under us
        ApplyPage();
        HasSongs = _all.Count > 0;
        IsEmpty = Loaded && _all.Count == 0;
    }

    // ---- adding songs -------------------------------------------------

    public async Task<bool> AddFileAsync()
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = Loc.Get("Library_FromFile"),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.audio" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.audio" } },
                    { DevicePlatform.WinUI, new[] { ".mp3", ".m4a", ".wav", ".aac", ".wma", ".aiff", ".flac" } },
                }),
            });
        }
        catch (Exception ex)
        {
            FileLog.Error("file picker", ex);
            Message?.Invoke(this, Loc.Get("Library_Err_File"));
            return false;
        }
        if (picked == null) return false;

        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "audio");
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid():N}{Path.GetExtension(picked.FileName)}";
            await using (var source = await picked.OpenReadAsync())
            await using (var target = File.Create(Path.Combine(dir, name)))
                await source.CopyToAsync(target);

            var song = new Song
            {
                Title = TitleFrom(picked.FileName),
                Source = SongSource.File,
                SourceRef = Path.Combine("audio", name),
                CreatedAt = DateTime.Now,
                Status = SongStatus.Pending,
            };
            await AddAndAnalyseAsync(song);
            return true;
        }
        catch (Exception ex)
        {
            FileLog.Error("add file", ex);
            Message?.Invoke(this, Loc.Get("Library_Err_File"));
            return false;
        }
    }

    /// <summary>Returns null on success or a Library_Err_* key.</summary>
    public async Task<string?> AddYouTubeAsync(string url)
    {
        if (!RemoteFlags.YouTubeAnalysis) return "Library_YT_Off";
        var id = _youtube.TryParseVideoId(url);
        if (id == null) return "Library_Err_NotLink";
        var existing = _all.FirstOrDefault(i => i.Song.Source == SongSource.YouTube && i.Song.SourceRef == id);
        if (existing != null)
        {
            Message?.Invoke(this, Loc.Get("Library_Duplicate"));
            if (existing.IsFailed) await RetryAsync(existing);
            return null;
        }

        YouTubeInfo info;
        try { info = await _youtube.GetInfoAsync(id); }
        catch (Exception ex)
        {
            FileLog.Error("youtube info", ex);
            return "Library_Err_YouTube";
        }

        string? thumb = Path.Combine("thumbs", $"{id}.jpg");
        if (!await _youtube.SaveThumbnailAsync(info, Path.Combine(FileSystem.AppDataDirectory, thumb)))
            thumb = null;

        var song = new Song
        {
            Title = info.Title,
            Artist = info.Author,
            Source = SongSource.YouTube,
            SourceRef = id,
            ThumbnailPath = thumb,
            DurationSec = info.Duration?.TotalSeconds ?? 0,
            CreatedAt = DateTime.Now,
            Status = SongStatus.Pending,
        };
        await AddAndAnalyseAsync(song);
        return null;
    }

    public async Task AddRecordingAsync(string path, double seconds)
    {
        int n = _all.Count(i => i.Song.Source == SongSource.Recording) + 1;
        var song = new Song
        {
            Title = $"{Loc.Get("Library_Take")} {n}",
            Source = SongSource.Recording,
            SourceRef = Path.GetRelativePath(FileSystem.AppDataDirectory, path),
            DurationSec = seconds,
            CreatedAt = DateTime.Now,
            Status = SongStatus.Pending,
        };
        await AddAndAnalyseAsync(song);
    }

    private async Task AddAndAnalyseAsync(Song song)
    {
        await _songs.InsertAsync(song);
        var item = new SongItem(song);
        _all.Insert(0, item);
        Rebuild();
        await StartAnalysisAsync(item);
    }

    private async Task StartAnalysisAsync(SongItem item)
    {
        var song = item.Song;
        if (!await _quota.CanStartAsync(song))
        {
            song.Status = SongStatus.Pending;
            song.Error = "Quota";
            await _songs.UpdateAsync(song);
            item.Refresh();
            ProRequired?.Invoke(this, Feature.UnlimitedSongs);
            return;
        }
        item.KeepOnCancel = song.Status == SongStatus.Ready;
        if (song.Status != SongStatus.Ready)
            await _quota.ConsumeAsync();
        song.Status = SongStatus.Analyzing;
        song.Error = null;
        item.Progress = 0;
        item.Refresh();
        _analysis.Enqueue(song.Id);
    }

    /// <summary>Free-tier status line for the add sheet; empty for Pro.</summary>
    public async Task<string> QuotaCaptionAsync()
    {
        if (_pro.Has(Feature.UnlimitedSongs)) return "";
        var state = await _quota.GetAsync();
        return FreeQuotaPolicy.IsDaily(state)
            ? Loc.Get("Library_QuotaDaily")
            : string.Format(Loc.Get("Library_QuotaLeft"), FreeQuotaPolicy.RemainingLifetime(state));
    }

    // ---- per-card actions ---------------------------------------------

    [RelayCommand]
    private async Task ToggleFavouriteAsync(SongItem item)
    {
        item.Song.Favourite = !item.Song.Favourite;
        item.Favourite = item.Song.Favourite;
        Haptics.Default.Selection();
        await _songs.UpdateAsync(item.Song);
        if (Filter == FilterFavourites) Rebuild();
    }

    [RelayCommand]
    private async Task DeleteAsync(SongItem item)
    {
        _all.Remove(item);
        Rebuild();
        await _analysis.RemoveAsync(item.Song);
    }

    /// <summary>× on an analysing card. The worker may be inside a long
    /// uninterruptible step (CQT of a whole song), so the card reacts at once
    /// and the service cleans up when the job unwinds.</summary>
    [RelayCommand]
    private void Cancel(SongItem item)
    {
        if (!_analysis.IsQueued(item.Id))
        {
            _ = DeleteAsync(item);
            return;
        }
        _analysis.Cancel(item.Id);
        if (item.KeepOnCancel)
        {
            item.ProgressText = Loc.Get("Library_Stage_Cancelling");
            return;
        }
        _all.Remove(item);
        Rebuild();
    }

    [RelayCommand]
    private Task RetryAsync(SongItem item) => StartAnalysisAsync(item);

    [RelayCommand]
    private void Open(SongItem item)
    {
        if (item.IsReady) OpenRequested?.Invoke(this, item);
        else if (item.IsFailed) _ = RetryAsync(item);
    }

    // ---- analysis events (main thread) --------------------------------

    private void OnProgress(int id, AnalysisStage stage, double progress) =>
        _all.FirstOrDefault(i => i.Id == id)?.SetProgress(stage, progress);

    private void OnFinished(int id, Song? song)
    {
        var item = _all.FirstOrDefault(i => i.Id == id);
        if (item == null) return;
        if (song == null)
        {
            _all.Remove(item);
        }
        else
        {
            item.Progress = 0;
            item.Update(song);
            if (song.Status == SongStatus.Ready) Haptics.Default.Success();
        }
        Rebuild();
    }

    private static string TitleFrom(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        return name.Length == 0 ? fileName : name;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
