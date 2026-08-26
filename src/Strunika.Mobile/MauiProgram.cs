using Microsoft.Extensions.Logging;
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
#elif WINDOWS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.Windows.WindowsMicrophoneSource>();
        // No Mica/acrylic backdrop: it bleeds a blurred desktop through any
        // transparent pixels (canvas corners, fades) on the dev head.
        builder.ConfigureLifecycleEvents(events => events.AddWindows(w => w.OnWindowCreated(window =>
            window.SystemBackdrop = null)));
        // WinUI's ToggleSwitch reserves ~154 px for on/off captions we never show.
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("CompactWidth", (handler, _) =>
        {
            handler.PlatformView.MinWidth = 0;
            handler.PlatformView.OnContent = null;
            handler.PlatformView.OffContent = null;
        });
#endif
        builder.Services.AddSingleton<IHaptics>(Haptics.Default);
        builder.Services.AddSingleton<DevProGate>();
        builder.Services.AddSingleton<IProGate>(sp => sp.GetRequiredService<DevProGate>());

        builder.Services.AddSingleton<TunerViewModel>();
        builder.Services.AddSingleton<LiveViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<RootPage>();
        builder.Services.AddTransient<WelcomePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
