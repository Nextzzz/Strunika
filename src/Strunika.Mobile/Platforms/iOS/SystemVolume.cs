using AVFoundation;
using CoreGraphics;
using MediaPlayer;
using UIKit;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// The device's output volume, read and set. A YouTube player in a web view
/// ignores setVolume on iOS (Apple leaves media volume to the person), so for
/// a YouTube song the page's slider drives this instead — the one way an app
/// may move the system volume is through MPVolumeView's own slider, kept off
/// screen here. Everything else the app plays goes through the same output,
/// so the metronome follows along; that is what the slider means for such a
/// song.
/// </summary>
public static class SystemVolume
{
    private static MPVolumeView? _view;
    private static UISlider? _slider;

    public static double Get()
    {
        try { return AVAudioSession.SharedInstance().OutputVolume; }
        catch { return 1; }
    }

    public static void Set(double value)
    {
        try
        {
            Ensure();
            if (_slider == null) return;
            _slider.Value = (float)Math.Clamp(value, 0, 1);
            _slider.SendActionForControlEvents(UIControlEvent.TouchUpInside);
        }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("system volume", ex); }
    }

    private static void Ensure()
    {
        if (_view != null && _slider != null) return;
        var window = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.FirstOrDefault();
        if (window == null) return;
        // Off screen and out of the way: it has to be in a window to work.
        _view = new MPVolumeView(new CGRect(-2000, -2000, 120, 40)) { ShowsRouteButton = false, ShowsVolumeSlider = true, Alpha = 0.01f };
        window.AddSubview(_view);
        _slider = _view.Subviews.OfType<UISlider>().FirstOrDefault();
    }
}
