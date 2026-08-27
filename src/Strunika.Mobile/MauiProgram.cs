using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Microsoft.Maui.LifecycleEvents;
using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Strunika.Core.Diagnostics.FileLog.Error("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Strunika.Core.Diagnostics.FileLog.Error("Unobserved task", e.Exception);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Display serif for chord names, the tuner note and large titles
                // (Vollkorn, OFL — Latin + Cyrillic). Everything else is the system font.
                fonts.AddFont("Vollkorn-SemiBold.ttf", "Display");
                fonts.AddFont("Vollkorn-Bold.ttf", "DisplayBold");
            });

#if IOS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.iOS.IosMicrophoneSource>();
        builder.Services.AddSingleton<IAudioDecoder, Platforms.iOS.IosAudioDecoder>();
        builder.Services.AddSingleton<ISoundPlayer, Platforms.iOS.IosSoundPlayer>();
        // UISwitch's off track is a faint grey that vanishes on the warm surfaces:
        // paint it with the Separator token (rounded background under the track).
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("OffTrack", (handler, _) =>
        {
            void Apply()
            {
                handler.PlatformView.BackgroundColor = Theme.Tokens.Current("Separator").ToPlatform();
                handler.PlatformView.Layer.CornerRadius = 15.5f;
                handler.PlatformView.ClipsToBounds = true;
            }
            Apply();
            AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.Theme)) Apply(); };
            if (Application.Current != null) Application.Current.RequestedThemeChanged += (_, _) => Apply();
        });
#elif WINDOWS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.Windows.WindowsMicrophoneSource>();
        builder.Services.AddSingleton<IAudioDecoder, Platforms.Windows.WindowsAudioDecoder>();
        builder.Services.AddSingleton<ISoundPlayer, Platforms.Windows.WindowsSoundPlayer>();
        // No Mica/acrylic backdrop: it bleeds a blurred desktop through any
        // transparent pixels (canvas corners, fades) on the dev head.
        builder.ConfigureLifecycleEvents(events => events.AddWindows(w => w.OnWindowCreated(window =>
            window.SystemBackdrop = null)));
        // No dotted keyboard-focus rectangles after navigation on the dev head (touch UI).
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping("NoFocusVisual", (handler, _) =>
            handler.PlatformView.UseSystemFocusVisuals = false);
        // WinUI's TextBox paints its own rounded border; our Entries sit inside
        // styled Borders already.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Flat", (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
                handler.PlatformView.Resources[key] = clear;
        });
        // WinUI's ToggleSwitch reserves ~154 px for on/off captions we never show.
        // Off-track colours need two homes: the control's own resources (read by
        // the Normal state, only writable once the control is loaded) and the
        // application ThemeDictionaries in Platforms/Windows/App.xaml (read by
        // the PointerOver/Pressed storyboards at template load).
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("CompactWidth", (handler, _) =>
        {
            handler.PlatformView.MinWidth = 0;
            handler.PlatformView.OnContent = null;
            handler.PlatformView.OffContent = null;
            void PaintOffTrack()
            {
                var res = handler.PlatformView.Resources;
                // WinUI quirk: the resting keys are Brushes, but the PointerOver /
                // Pressed keys are plain Colors (the storyboards animate Fill with a
                // Color) — a Brush under those keys renders as nothing.
                foreach (var (key, token, isBrush) in new[]
                {
                    ("ToggleSwitchFillOff", "Separator", true), ("ToggleSwitchFillOffPointerOver", "Separator", false), ("ToggleSwitchFillOffPressed", "Separator", false),
                    ("ToggleSwitchStrokeOff", "Dim", true), ("ToggleSwitchStrokeOffPointerOver", "Dim", false), ("ToggleSwitchStrokeOffPressed", "Dim", false),
                })
                {
                    var colour = Theme.Tokens.Current(token).ToWindowsColor();
                    try
                    {
                        if (isBrush && res.TryGetValue(key, out var existing) && existing is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                            brush.Color = colour;
                        else if (isBrush)
                            res[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(colour);
                        else
                            res[key] = colour;
                    }
                    catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error($"switch resource {key}", ex); }
                }
            }
            if (handler.PlatformView.IsLoaded) PaintOffTrack();
            else handler.PlatformView.Loaded += (_, _) => PaintOffTrack();
            AppSettings.Changed += (_, key) => { if (key == nameof(AppSettings.Theme)) PaintOffTrack(); };
            if (Application.Current != null) Application.Current.RequestedThemeChanged += (_, _) => PaintOffTrack();
        });
#endif
        builder.Services.AddSingleton<IHaptics>(Haptics.Default);
        builder.Services.AddSingleton<DevProGate>();
        builder.Services.AddSingleton<IProGate>(sp => sp.GetRequiredService<DevProGate>());

        // Song library (M2): SQLite, background analysis queue, free quota.
        builder.Services.AddSingleton<Data.ISongRepository, Data.SongRepository>();
        builder.Services.AddSingleton<IYouTubeSource, YoutubeExplodeSource>();
        builder.Services.AddSingleton<AnalysisService>();
        builder.Services.AddSingleton<FreeQuota>();
        builder.Services.AddSingleton<TakeRecorder>();
        builder.Services.AddSingleton<LibraryViewModel>();

        builder.Services.AddSingleton<TunerViewModel>();
        builder.Services.AddSingleton<LiveViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<RootPage>();
        builder.Services.AddTransient<WelcomePage>();
        builder.Services.AddTransient<LaunchPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
