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
/// <para>
/// While an MPVolumeView is in the window iOS hides its own volume overlay,
/// so the view is only there while our slider is on screen (<see cref="Attach"/>
/// … <see cref="Detach"/>): elsewhere the volume buttons show the system's
/// overlay as usual, and while our slider shows, the buttons move it instead.
/// </para>
/// </summary>
public static class SystemVolume
{
    private static MPVolumeView? _view;
    private static UISlider? _slider;
    private static IDisposable? _observer;
    private static Action<double>? _changed;

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

    /// <summary>Our slider is on screen: take over the overlay, and report the
    /// buttons' changes through <paramref name="changed"/> (main thread).</summary>
    public static void Attach(Action<double> changed)
    {
        _changed = changed;
        try
        {
            Ensure();
            AddToWindow();
            if (_observer != null) return;
            // outputVolume only reports while a session is active — the mixable
            // one: activating any other pauses the video this slider sits over.
            AudioSessions.ForPlayback();
            var session = AVAudioSession.SharedInstance();
            _observer = session.AddObserver("outputVolume", Foundation.NSKeyValueObservingOptions.New, change =>
            {
                if (change.NewValue is Foundation.NSNumber n)
                {
                    double v = n.DoubleValue;
                    MainThread.BeginInvokeOnMainThread(() => _changed?.Invoke(v));
                }
            });
        }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("system volume attach", ex); }
    }

    /// <summary>Our slider is gone: give the overlay back to the system. The
    /// volume view leaves the window but is never destroyed, and neither is the
    /// observer: an MPVolumeView keeps watchers of its own on the route and the
    /// session, and disposing one under them crashes later, somewhere else
    /// (the same lesson as Observers). Out of the window it does nothing and
    /// costs nothing.</summary>
    public static void Detach()
    {
        _changed = null;
        try { _view?.RemoveFromSuperview(); }
        catch (Exception ex) { Strunika.Core.Diagnostics.FileLog.Error("system volume detach", ex); }
    }

    private static void Ensure()
    {
        if (_view != null && _slider != null) return;
        // Off screen and out of the way: it has to be in a window to work.
        _view = new MPVolumeView(new CGRect(-2000, -2000, 120, 40)) { Alpha = 0.01f };   // the slider is what it shows by default
        AddToWindow();
        _slider = _view.Subviews.OfType<UISlider>().FirstOrDefault();
    }

    private static void AddToWindow()
    {
        if (_view == null || _view.Superview != null) return;
        var window = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow);
        window?.AddSubview(_view);
    }
}
