using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Strunika.Mobile.Localization;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;

namespace Strunika.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly DevProGate _devPro;
    private readonly IProGate _pro;

    public SettingsViewModel(DevProGate devPro, IProGate pro)
    {
        _devPro = devPro;
        _pro = pro;
        Loc.Instance.PropertyChanged += (_, _) => RefreshNames();
        _pro.Changed += (_, _) => { OnPropertyChanged(nameof(A4Locked)); OnPropertyChanged(nameof(AltTuningsLocked)); };
        AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.A4Reference)) OnPropertyChanged(nameof(A4Text)); };
        RefreshNames();
    }

    // ---- appearance --------------------------------------------------

    public string ThemeName => AppSettings.Theme switch
    {
        "dark" => Loc.Get("Theme_Dark"),
        "light" => Loc.Get("Theme_Light"),
        _ => Loc.Get("Theme_System"),
    };

    public string ThemeIcon => AppSettings.Theme switch { "dark" => "moon", "light" => "sun", _ => "auto" };

    public string LanguageName => Loc.Instance.LanguageCode == "uk" ? Loc.Get("Lang_Uk") : Loc.Get("Lang_En");

    public string LanguageFlag => Loc.Instance.LanguageCode == "uk" ? "flag_ua" : "flag_gb";

    public void SetTheme(int index)
    {
        AppSettings.ThemeIndex = index;
        RefreshNames();
    }

    public void SetLanguage(string code)
    {
        Loc.Instance.Culture = new CultureInfo(code);
        RefreshNames();
    }

    public bool SkipWelcome
    {
        get => AppSettings.SkipWelcome;
        set { AppSettings.SkipWelcome = value; OnPropertyChanged(); }
    }

    public bool LeftHanded
    {
        get => AppSettings.LeftHanded;
        set { AppSettings.LeftHanded = value; OnPropertyChanged(); }
    }

    // ---- tuner -------------------------------------------------------

    public string A4Text => $"{AppSettings.A4Reference:0} {Loc.Get("Unit_Hz")}";

    public bool A4Locked => !_pro.Has(Feature.A4Reference);

    public bool AltTuningsLocked => !_pro.Has(Feature.AltTunings);

    public string DefaultTuningName => Models.Tuning.ById(AppSettings.DefaultTuning).Name;

    public void SetDefaultTuning(string id)
    {
        AppSettings.DefaultTuning = id;
        OnPropertyChanged(nameof(DefaultTuningName));
    }

    // ---- recognition -------------------------------------------------

    public bool BeatSnap
    {
        get => AppSettings.BeatSnap;
        set { AppSettings.BeatSnap = value; OnPropertyChanged(); }
    }

    public bool Expert
    {
        get => AppSettings.Expert;
        set { AppSettings.Expert = value; OnPropertyChanged(); }
    }

    public bool DevProAvailable => DevProGate.IsAvailable;

    public bool DevPro
    {
        get => _devPro.IsPro;
        set { _devPro.IsPro = value; OnPropertyChanged(); }
    }

    // ---- about -------------------------------------------------------

    public string Version => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    private void RefreshNames()
    {
        OnPropertyChanged(nameof(ThemeName));
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(LanguageName));
        OnPropertyChanged(nameof(LanguageFlag));
        OnPropertyChanged(nameof(DefaultTuningName));
        OnPropertyChanged(nameof(A4Text));
    }
}
