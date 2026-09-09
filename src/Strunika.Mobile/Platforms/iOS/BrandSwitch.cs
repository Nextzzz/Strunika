using Microsoft.Maui.Handlers;
using Strunika.Mobile.Services;
using UIKit;

namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// A UISwitch whose off track keeps the brand colour through every restyling
/// UIKit does to its insides — the first time it is shown, a return from the
/// background, a trait change — because it repaints itself after each layout
/// (<see cref="SwitchPaint.Paint(UISwitch)"/> is a no-op when nothing changed).
/// Painted only at handler time, a switch came up with the system's faint grey
/// track and wore it until it was toggled.
/// </summary>
public sealed class BrandSwitch : UISwitch
{
    public BrandSwitch() : base(CoreGraphics.CGRect.Empty) { }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        SwitchPaint.Paint(this);
    }
}

/// <summary>
/// Creates <see cref="BrandSwitch"/> and wires the value change itself. MAUI's
/// own wiring also repaints an off switch with its <i>on</i> colour ten
/// milliseconds after the app returns to the foreground (its answer to UIKit
/// restyling the track), which is how off switches came back gold; the base
/// ConnectHandler does nothing else, so it is not called.
/// </summary>
public sealed class BrandSwitchHandler : SwitchHandler
{
    protected override UISwitch CreatePlatformView() => new BrandSwitch();

    protected override void ConnectHandler(UISwitch platformView) => platformView.ValueChanged += OnValueChanged;

    protected override void DisconnectHandler(UISwitch platformView)
    {
        platformView.ValueChanged -= OnValueChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnValueChanged(object? sender, EventArgs e)
    {
        if (sender is UISwitch view && VirtualView is { } virtualView && virtualView.IsOn != view.On)
            virtualView.IsOn = view.On;
    }
}
