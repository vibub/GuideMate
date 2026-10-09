using System.Windows.Controls.Primitives;

namespace GuideMate.App;

internal sealed partial class SubtitleWindow : Window
{
    private readonly TextBlock _direction = Ui.Text("", 21, Brushes.White);
    private readonly TextBlock _subtitle = Ui.Text("", 16, Brushes.White);
    private readonly TextBlock _kind = Ui.Text("", 11, new SolidColorBrush(Color.FromRgb(168, 217, 198)));
    private readonly Grid _header = new();
    private readonly ScrollViewer _captionHost = new() { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _empty = Ui.Text("等待字幕或方向提示", 14, new SolidColorBrush(Color.FromRgb(168, 217, 198)));
    private readonly Thumb _widthGrip = new() { Width = 22, Height = 20, Cursor = Cursors.SizeWE,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        ToolTip = "长按后左右拖动调整字幕宽度，高度随字幕自动适应" };
    private double _resizeOriginX, _resizeRightInset;
    public event Action? PlacementChanged;
    public SubtitleWindow()
    {
        Title = "随引字幕与方向";
        Width = 520; SizeToContent = SizeToContent.Height; MinWidth = 260; MinHeight = 56;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; WindowStyle = WindowStyle.None;
        SourceInitialized += (_, _) =>
        {
            NativeHotkeys.HideFromWindowSwitcher(this, true);
            NativeHotkeys.ClickThrough(this, true);
        };
        AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        var grid = new Grid { Margin = new(14, 10, 14, 20) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _direction.TextWrapping = TextWrapping.NoWrap;
        _kind.TextWrapping = TextWrapping.NoWrap; _kind.TextTrimming = TextTrimming.CharacterEllipsis;
        _kind.Margin = new(12, 0, 0, 0);
        _header.Children.Add(_direction); Grid.SetColumn(_kind, 1); _header.Children.Add(_kind);
        grid.Children.Add(_header);
        _subtitle.VerticalAlignment = VerticalAlignment.Top;
        _captionHost.Content = _subtitle; _captionHost.Margin = new(0, 6, 0, 0);
        Grid.SetRow(_captionHost, 1); grid.Children.Add(_captionHost); grid.Children.Add(_empty);
        var gripSurface = new FrameworkElementFactory(typeof(Border));
        gripSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        // Match the triangular dot pattern of the original WPF resize grip; the Thumb still resizes width only.
        var gripForeground = new LinearGradientBrush
        {
            StartPoint = new(0, 0.25), EndPoint = new(1, 0.75),
            GradientStops = [new(Colors.White, 0.3), new(Color.FromRgb(187, 197, 215), 0.75), new(Color.FromRgb(109, 131, 169), 1)]
        };
        var gripPattern = new DrawingBrush(new GeometryDrawing(gripForeground, null, Geometry.Parse("M 0,0 L 2,0 L 2,2 L 0,2 Z")))
        {
            Viewbox = new(0, 0, 3, 3), Viewport = new(0, 0, 3, 3),
            ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile
        };
        var gripIcon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        gripIcon.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 9,0 L 11,0 L 11,11 L 0,11 L 0,9 L 3,9 L 3,6 L 6,6 L 6,3 L 9,3 Z"));
        gripIcon.SetValue(System.Windows.Shapes.Path.FillProperty, gripPattern);
        gripIcon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        gripIcon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        gripIcon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 2));
        gripIcon.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        gripIcon.SetValue(UIElement.IsHitTestVisibleProperty, false);
        gripSurface.AppendChild(gripIcon);
        _widthGrip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripSurface };
        _widthGrip.DragStarted += (_, e) =>
        {
            _resizeOriginX = e.HorizontalOffset;
            _resizeRightInset = ActualWidth - _widthGrip.TranslatePoint(new(_resizeOriginX, 0), this).X;
        };
        _widthGrip.DragDelta += (_, e) =>
        {
            // Use the grip's current position so queued deltas do not accumulate unapplied widths.
            var pointerX = _widthGrip.TranslatePoint(new(_resizeOriginX + e.HorizontalChange, 0), this).X;
            Width = Math.Max(MinWidth, pointerX + _resizeRightInset);
        };
        _widthGrip.DragCompleted += (_, e) => { if (!e.Canceled) PlacementChanged?.Invoke(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _widthGrip.CancelDrag(); };
        var surface = new Grid();
        surface.Children.Add(grid); surface.Children.Add(_widthGrip);
        Content = new Border { CornerRadius = new(4), Background = new SolidColorBrush(Color.FromArgb(228, 28, 32, 33)), Child = surface };
        InitializePointerGesture();
        Update("", null);
    }
    public void ApplySettings(AppSettings settings)
    {
        Opacity = settings.SubtitleOpacity;
        _subtitle.FontSize = settings.SubtitleFontSize;
        _direction.FontSize = settings.DirectionFontSize;
    }
    private string? _lastText;
    private DirectionHint? _lastHint;
    public void Update(string text, DirectionHint? hint)
    {
        if (_lastText == text && _lastHint == hint) return;
        _lastText = text; _lastHint = hint;
        _direction.Text = hint?.Label ?? "";
        _kind.Text = hint?.Category ?? "字幕";
        _kind.ToolTip = hint?.Category;
        _subtitle.Text = text;
        _header.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
        _captionHost.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        _captionHost.Margin = new(0, hint == null ? 0 : 6, 0, 0);
        _empty.Visibility = hint == null && string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
    }

}
