namespace Strunika.Mobile.Controls;

/// <summary>
/// Small gold lock that marks a Pro feature on a visible control (design
/// rule: locked controls stay visible; tapping them opens the paywall).
/// Place it in the same Grid cell as the control, top-right, with
/// <c>Margin="0,-6,-6,0"</c>.
/// </summary>
public sealed class LockBadge : Border
{
    public static readonly BindableProperty IsLockedProperty =
        BindableProperty.Create(nameof(IsLocked), typeof(bool), typeof(LockBadge), true,
            propertyChanged: (b, o, n) => ((LockBadge)b).IsVisible = (bool)n);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(LockBadge), Colors.Black,
            propertyChanged: (b, o, n) => ((LockBadge)b)._icon.Color = (Color)n);

    public bool IsLocked { get => (bool)GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
    public Color IconColor { get => (Color)GetValue(IconColorProperty); set => SetValue(IconColorProperty, value); }

    private readonly IconView _icon = new() { Name = "lock", Size = 11 };

    public LockBadge()
    {
        WidthRequest = HeightRequest = 18;
        StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 };
        Padding = 0;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        InputTransparent = true;
        Content = _icon;
        _icon.HorizontalOptions = _icon.VerticalOptions = LayoutOptions.Center;
    }
}
