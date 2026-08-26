using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Strunika.Mobile.Localization;

/// <summary>
/// Runtime string lookup with live language switching. Bind through the
/// indexer (<c>{loc:Str Key}</c>) — changing <see cref="Culture"/> raises
/// a blanket PropertyChanged so every bound label re-reads its text
/// without rebuilding the page.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string PreferenceKey = "ui_language";
    public static readonly string[] Supported = { "uk", "en" };

    public static Loc Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("Strunika.Mobile.Resources.Strings.Strings", typeof(Loc).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private Loc()
    {
        string saved = Preferences.Default.Get(PreferenceKey, "");
        string code = Supported.Contains(saved) ? saved : DetectDefault();
        Apply(new CultureInfo(code));
    }

    /// <summary>Ukrainian for Ukrainian devices, English for everyone else —
    /// the same default the first-launch screen pre-selects.</summary>
    public static string DetectDefault() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk" ? "uk" : "en";

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (value.Name == _culture.Name)
                return;
            Apply(value);
            Preferences.Default.Set(PreferenceKey, value.TwoLetterISOLanguageName);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    public string LanguageCode => _culture.TwoLetterISOLanguageName;

    private void Apply(CultureInfo culture)
    {
        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public string this[string key] =>
        _resources.GetString(key, _culture) ?? $"[{key}]";

    /// <summary>Formatted lookup for code-behind / view-models.</summary>
    public static string Get(string key, params object[] args)
    {
        string s = Instance[key];
        return args.Length == 0 ? s : string.Format(Instance._culture, s, args);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
