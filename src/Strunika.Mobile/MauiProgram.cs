using Microsoft.Extensions.Logging;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if IOS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.iOS.IosMicrophoneSource>();
#elif WINDOWS
        builder.Services.AddSingleton<IMicrophoneSource, Platforms.Windows.WindowsMicrophoneSource>();
#endif
        builder.Services.AddSingleton<TunerViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
