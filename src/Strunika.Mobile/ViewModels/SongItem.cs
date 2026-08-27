using CommunityToolkit.Mvvm.ComponentModel;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

/// <summary>A library card: the song plus its live analysis state.</summary>
public sealed partial class SongItem : ObservableObject
{
    public SongItem(Song song)
    {
        Song = song;
        Refresh();
    }

    public Song Song { get; private set; }
    public int Id => Song.Id;

    /// <summary>Re-analysis of a song that already has chords: cancelling keeps the card.</summary>
    public bool KeepOnCancel { get; set; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _keyText = "";
    [ObservableProperty] private string _metaText = "";
    [ObservableProperty] private bool _favourite;
    [ObservableProperty] private bool _edited;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isWaiting;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _failText = "";
    [ObservableProperty] private string _glyph = "file";
    [ObservableProperty] private string? _thumbnail;
    [ObservableProperty] private bool _hasThumbnail;

    public void Update(Song song)
    {
        Song = song;
        Refresh();
    }

    public void Refresh()
    {
        var s = Song;
        Title = s.Title;
        Subtitle = s.Source switch
        {
            SongSource.Recording => $"{Loc.Get("Library_Source_Recording")} · {s.CreatedAt.ToString("d MMM", Loc.Instance.Culture)}",
            _ => string.IsNullOrWhiteSpace(s.Artist) ? Loc.Get(s.Source == SongSource.YouTube ? "Library_Source_YouTube" : "Library_Source_File") : s.Artist,
        };
        Favourite = s.Favourite;
        Edited = s.Edited;
        Glyph = s.Source switch { SongSource.YouTube => "youtube", SongSource.Recording => "mic", _ => "file" };
        Thumbnail = string.IsNullOrEmpty(s.ThumbnailPath) ? null : Path.Combine(FileSystem.AppDataDirectory, s.ThumbnailPath);
        HasThumbnail = Thumbnail != null && File.Exists(Thumbnail);

        IsReady = s.Status == SongStatus.Ready;
        IsAnalyzing = s.Status == SongStatus.Analyzing || (s.Status == SongStatus.Pending && s.Error == null);
        IsFailed = s.Status == SongStatus.Failed || (s.Status == SongStatus.Pending && s.Error != null);
        IsWaiting = false;
        KeyText = s.Key ?? "";
        MetaText = IsReady ? Meta(s) : "";
        FailText = IsFailed ? Loc.Get("Library_Err_" + (s.Error ?? "Unknown")) : "";
        if (IsAnalyzing && Progress == 0)
            ProgressText = Loc.Get("Library_Stage_Queued");
    }

    public void SetProgress(AnalysisStage stage, double value)
    {
        IsAnalyzing = true;
        IsFailed = false;
        Progress = value;
        // Every working stage reads "Analysing · N %": what happens to the audio
        // on the way is an implementation detail, not something to narrate.
        bool working = stage is AnalysisStage.Downloading or AnalysisStage.Decoding or AnalysisStage.Recognizing
                             or AnalysisStage.Beats or AnalysisStage.Saving;
        ProgressText = working
            ? $"{Loc.Get("Library_Stage_Recognizing")} · {value * 100:0} %"
            : Loc.Get("Library_Stage_" + stage);
    }

    private static string Meta(Song s)
    {
        var parts = new List<string>();
        if (s.Bpm > 0) parts.Add($"♩ {s.Bpm:0}");
        if (s.DurationSec > 0) parts.Add(Duration(s.DurationSec));
        return string.Join("  ·  ", parts);
    }

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
