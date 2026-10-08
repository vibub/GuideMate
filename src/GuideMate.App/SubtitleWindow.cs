namespace GuideMate.App;

internal sealed class SubtitleWindow : Window
{
    private readonly TextBlock _direction = Ui.Text("", 21, Brushes.White);
    private readonly TextBlock _subtitle = Ui.Text("", 16, Brushes.White);
    private readonly TextBlock _kind = Ui.Text("", 11, new SolidColorBrush(Color.FromRgb(168, 217, 198)));
    private readonly Grid _header = new();
    private readonly ScrollViewer _captionHost = new() { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _empty = Ui.Text("等待字幕或方向提示", 14, new SolidColorBrush(Color.FromRgb(168, 217, 198)));
    public SubtitleWindow()
    {
        Title = "随引字幕与方向";
        Width = 520; SizeToContent = SizeToContent.Height; MinWidth = 260; MinHeight = 56;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; WindowStyle = WindowStyle.None;
        SourceInitialized += (_, _) => NativeHotkeys.HideFromWindowSwitcher(this, true);
        AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
        var grid = new Grid { Margin = new(14, 10, 14, 10) };
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
        Content = new Border { CornerRadius = new(4), Background = new SolidColorBrush(Color.FromArgb(228, 28, 32, 33)), Child = grid };
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) DragMove(); };
        Update("", null);
    }
    private string? _lastText;
    private DirectionHint? _lastHint;
    public void Update(string text, DirectionHint? hint)
    {
        if (_lastText == text && _lastHint == hint) return;
        _lastText = text; _lastHint = hint;
        SizeToContent = SizeToContent.Height;
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
