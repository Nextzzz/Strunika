using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using Strunika.Mobile.Models;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

/// <summary>One chord in the dictionary: its name and the shape shown on the card.</summary>
public sealed record ChordEntry(string Label, ChordShape? Shape);

/// <summary>All chords sharing a root, e.g. every "A…" chord.</summary>
public sealed class ChordGroup : List<ChordEntry>
{
    public ChordGroup(string root, IEnumerable<ChordEntry> entries) : base(entries) => Root = root;
    public string Root { get; }
}

/// <summary>
/// The chord dictionary: everything the recogniser can name, grouped by root
/// (alphabetically from A) and filterable. "Simple" keeps only the qualities a
/// simplified timeline can produce — the vocabulary a beginner sees — while the
/// full list adds the sevenths, sixths and sus chords the models output.
/// <para>
/// The list is rebuilt as a whole new collection, never by clearing the bound
/// one: mutating a grouped ItemsSource in place crashes the WinUI list. Typing
/// is debounced and the shapes are computed once and cached, so a keystroke
/// costs a filter over 168 ready-made entries.
/// </para>
/// </summary>
public sealed partial class ChordDictionaryViewModel : ObservableObject
{
    /// <summary>Roots as the recogniser names them, alphabetically from A.</summary>
    private static readonly string[] Roots = { "A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#" };

    /// <summary>The 14 qualities of the 170-class models, in teaching order.</summary>
    private static readonly string[] Qualities = { "", "m", "7", "m7", "maj7", "6", "m6", "sus2", "sus4", "dim", "dim7", "m7b5", "aug", "mmaj7" };

    /// <summary>What survives <c>ChordLabels.Simplify</c>: triads only.</summary>
    private static readonly string[] SimpleQualities = { "", "m", "dim", "aug" };

    private static readonly ConcurrentDictionary<string, ChordEntry> Cache = new();

    private CancellationTokenSource? _typing;

    public ChordDictionaryViewModel()
    {
        Rebuild();
        AppSettings.Changed += OnSettingsChanged;
    }

    [ObservableProperty] private IReadOnlyList<ChordGroup> _groups = Array.Empty<ChordGroup>();
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _simple = AppSettings.SimpleChords;
    [ObservableProperty] private bool _isEmpty;

    /// <summary>A keystroke waits a moment: filtering on every character while the
    /// list rebuilds is what made typing stutter.</summary>
    partial void OnQueryChanged(string value)
    {
        _typing?.Cancel();
        _typing = new CancellationTokenSource();
        var token = _typing.Token;
        Task.Delay(180, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => { if (!token.IsCancellationRequested) Rebuild(); });
        }, TaskScheduler.Default);
    }

    partial void OnSimpleChanged(bool value)
    {
        AppSettings.SimpleChords = value;                          // the same setting as everywhere else
        Rebuild();
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == nameof(AppSettings.SimpleChords) && Simple != AppSettings.SimpleChords) Simple = AppSettings.SimpleChords;
    }

    public void Detach()
    {
        _typing?.Cancel();
        AppSettings.Changed -= OnSettingsChanged;
    }

    private static ChordEntry Entry(string label) => Cache.GetOrAdd(label, l => new ChordEntry(l, ChordShapes.For(l)));

    private void Rebuild()
    {
        var qualities = Simple ? SimpleQualities : Qualities;
        var query = Query.Trim().Replace('♯', '#').Replace('♭', 'b');
        var groups = new List<ChordGroup>(Roots.Length);
        foreach (var root in Roots)
        {
            List<ChordEntry>? entries = null;
            foreach (var quality in qualities)
            {
                var label = root + quality;
                if (query.Length > 0 && !label.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                (entries ??= new List<ChordEntry>(qualities.Length)).Add(Entry(label));
            }
            if (entries != null) groups.Add(new ChordGroup(Pretty(root), entries));
        }
        Groups = groups;
        IsEmpty = groups.Count == 0;
    }

    /// <summary>Sharps read better as ♯ in a heading.</summary>
    private static string Pretty(string root) => root.Replace("#", "♯");
}
