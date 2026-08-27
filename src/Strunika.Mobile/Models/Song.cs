using System.Text.Json;
using SQLite;

namespace Strunika.Mobile.Models;

public enum SongSource { File = 0, Recording = 1, YouTube = 2 }

public enum SongStatus { Pending = 0, Analyzing = 1, Ready = 2, Failed = 3 }

/// <summary>One chord of a song's timeline, seconds from the start.</summary>
public sealed record ChordSegmentDto(double Start, double End, string Label);

/// <summary>
/// A library entry. Chords live in a JSON column (a timeline is always read
/// and written whole). <see cref="SourceRef"/> is a path relative to the
/// app data directory for files and recordings, the video id for YouTube —
/// YouTube audio is never kept (product decision: extraction "like
/// ChordAI", playback through the official embed).
/// </summary>
[Table("songs")]
public sealed class Song
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public SongSource Source { get; set; }
    public string SourceRef { get; set; } = "";
    public string? ThumbnailPath { get; set; }
    public double DurationSec { get; set; }
    public string? Key { get; set; }
    public double Bpm { get; set; }
    [Indexed]
    public DateTime CreatedAt { get; set; }
    public bool Favourite { get; set; }
    public int? FolderId { get; set; }
    public bool Edited { get; set; }
    public SongStatus Status { get; set; }
    public string? Error { get; set; }
    /// <summary>Model that produced the timeline (e.g. "btc_self"), for re-analysis prompts.</summary>
    public string? Model { get; set; }
    public string SegmentsJson { get; set; } = "[]";

    [Ignore]
    public IReadOnlyList<ChordSegmentDto> Segments
    {
        get => JsonSerializer.Deserialize<List<ChordSegmentDto>>(SegmentsJson) ?? new List<ChordSegmentDto>();
        set => SegmentsJson = JsonSerializer.Serialize(value);
    }

    [Ignore]
    public bool IsReady => Status == SongStatus.Ready;
}
