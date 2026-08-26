using Strunika.Mobile.Services;

namespace Strunika.Mobile.Controls;

public sealed class SegmentItem
{
    public SegmentItem(string text, string? icon = null) { Text = text; Icon = icon; }
    public string Text { get; set; }
    public string? Icon { get; }
}

/// <summary>
/// iOS-style segmented control drawn with the brand tokens: a Surface1
/// track, the selected segment on Surface2, optional icon per segment
/// (stroke icon or flag via <see cref="IconView"/>).
/// </summary>
public sealed class Segmented : ContentView
{
    public static readonly BindableProperty SelectedIndexProperty =
        BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(Segmented), 0, BindingMode.TwoWay,
            propertyChanged: (b, o, n) => ((Segmented)b).Render());

    public static readonly BindableProperty TrackColorProperty = Col(nameof(TrackColor));
    public static readonly BindableProperty SeparatorColorProperty = Col(nameof(SeparatorColor));
    public static readonly BindableProperty SelectedColorProperty = Col(nameof(SelectedColor));
    public static readonly BindableProperty TextColorProperty = Col(nameof(TextColor));
    public static readonly BindableProperty InactiveTextColorProperty = Col(nameof(InactiveTextColor));

    private static BindableProperty Col(string name) =>
        BindableProperty.Create(name, typeof(Color), typeof(Segmented), Colors.Gray,
            propertyChanged: (b, o, n) => ((Segmented)b).Render());

    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public Color TrackColor { get => (Color)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public Color SeparatorColor { get => (Color)GetValue(SeparatorColorProperty); set => SetValue(SeparatorColorProperty, value); }
    public Color SelectedColor { get => (Color)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color InactiveTextColor { get => (Color)GetValue(InactiveTextColorProperty); set => SetValue(InactiveTextColorProperty, value); }

    private readonly List<SegmentItem> _items = new();
    private readonly Grid _grid = new() { ColumnSpacing = 2, Padding = 3, VerticalOptions = LayoutOptions.Center };
    private readonly Border _track = new() { StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Padding = 0 };

    public Segmented()
    {
        _track.Content = _grid;
        Content = _track;
        // 36-pt cells + 3-pt track padding + 1-pt stroke, with slack so no
        // platform rounds the bottom edge away.
        HeightRequest = 48;
        _track.HeightRequest = 46;
    }

    public void SetItems(IEnumerable<SegmentItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Render();
    }

    /// <summary>Update labels in place (language change) without rebuilding.</summary>
    public void SetLabels(params string[] labels)
    {
        for (int i = 0; i < Math.Min(labels.Length, _items.Count); i++)
            _items[i].Text = labels[i];
        Render();
    }

    private void Render()
    {
        _track.BackgroundColor = TrackColor;
        _track.Stroke = SeparatorColor;
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            bool selected = i == SelectedIndex;
            var row = new HorizontalStackLayout { Spacing = 7, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            if (_items[i].Icon != null)
                row.Add(new IconView { Name = _items[i].Icon!, Size = 17, Color = selected ? TextColor : InactiveTextColor });
            row.Add(new Label
            {
                Text = _items[i].Text,
                FontSize = 14,
                FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                TextColor = selected ? TextColor : InactiveTextColor,
                VerticalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
            });
            var cell = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
                BackgroundColor = selected ? SelectedColor : Colors.Transparent,
                Content = row,
                HeightRequest = 36,
                Padding = new Thickness(6, 0),
                VerticalOptions = LayoutOptions.Center,
            };
            int index = i;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (SelectedIndex == index) return;
                Haptics.Default.Selection();
                SelectedIndex = index;
            };
            cell.GestureRecognizers.Add(tap);
            _grid.Add(cell, i, 0);
        }
    }
}
