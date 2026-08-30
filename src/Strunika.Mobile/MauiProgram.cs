using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;

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
        builder.Services.AddTransient<IAudioPlayer, Platforms.iOS.IosAudioPlayer>();
        builder.Services.AddSingleton<IClickPlayer, Platforms.iOS.IosClickPlayer>();
        builder.Services.AddSingleton<IChordAudio, Platforms.iOS.IosChordAudio>();
        // UISwitch's off track is a faint grey that vanishes on the warm surfaces:
        // paint it with the Separator token (rounded background under the track).
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("OffTrack", (handler, _) => SwitchPaint.Track(handler.PlatformView));
#elif WINDOWS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.Windows.WindowsMicrophoneSource>();
        builder.Services.AddSingleton<IAudioDecoder, Platforms.Windows.WindowsAudioDecoder>();
        builder.Services.AddSingleton<ISoundPlayer, Platforms.Windows.WindowsSoundPlayer>();
        builder.Services.AddTransient<IAudioPlayer, Platforms.Windows.WindowsAudioPlayer>();
        builder.Services.AddSingleton<IClickPlayer, Platforms.Windows.WindowsClickPlayer>();
        builder.Services.AddSingleton<IChordAudio, Platforms.Windows.WindowsChordAudio>();
        // The YouTube embed is driven programmatically: WebView2 must allow play() without a gesture.
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
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
        // The theme is watched once, for all switches at once, through weak
        // references: a subscription per handler outlived its control, and
        // repainting after the page had closed threw "PlatformView cannot be
        // null here" (and kept every switch ever created in memory).
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("CompactWidth", (handler, _) =>
        {
            var view = handler.PlatformView;
            view.MinWidth = 0;
            view.OnContent = null;
            view.OffContent = null;
            SwitchPaint.Track(view);
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
