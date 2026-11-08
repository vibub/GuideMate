using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GuideMate.Vision;

namespace GuideMate.App;

/// <summary>与方向校准一致：框内移动、边角缩放、框外重画，保存时才应用。</summary>
internal sealed partial class MapRegionWindow : Window
{
    private readonly Canvas _selection = new() { Background = Brushes.Transparent };
    private readonly Rectangle _regionOutline = new() { Stroke = Brushes.OrangeRed,
        Fill = new SolidColorBrush(Color.FromArgb(25, 255, 90, 30)), IsHitTestVisible = false };
    private readonly TextBlock _coordinates = Ui.Text("", 12, Ui.Muted);
    private readonly VideoInfo _info;
    private ArrowRegion _region;
    public ArrowRegion? Result { get; private set; }

    public MapRegionWindow(Window owner, byte[] image, string title, ArrowRegion? previous)
    {
        Owner = owner; Title = title; Width = 1000; Height = 720; MinWidth = 680; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        using var stream = new MemoryStream(image);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        _info = new(bitmap.PixelWidth, bitmap.PixelHeight, 0);
        var side = Math.Min(_info.Width, _info.Height) * .3;
        _region = previous is { IsValid: true } ? previous : new(.05, .05, side / _info.Width, side / _info.Height);
        var picture = new Grid { Width = _info.Width, Height = _info.Height };
        picture.Children.Add(new Image { Source = bitmap }); picture.Children.Add(_selection);
        _selection.Children.Add(_regionOutline); InitializeSelection();
        var root = new DockPanel { Margin = new(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.Command("\uE74E", "保存选区", () =>
        {
            if (!_region.IsValid || Math.Min(_region.Width * _info.Width, _region.Height * _info.Height) < 96)
            { MessageBox.Show(this, "请框选完整小地图，原始宽度至少 96 像素。", "地图校准"); return; }
            Result = _region; DialogResult = true;
        }));
        actions.Children.Add(Ui.Command("\uE711", "取消", () => DialogResult = false));
        actions.Children.Add(_coordinates); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var help = Ui.Text("框选完整小地图：框内拖动移动，拖动八个边角调整大小，框外拖动重新框选。Esc 撤销本次拖动。", 14);
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        var viewbox = new Viewbox { Child = picture, Stretch = Stretch.Uniform, Margin = new(0, 10, 0, 10) };
        viewbox.SizeChanged += (_, _) => { EndSelectionDrag(true); DrawRegion(); };
        root.Children.Add(viewbox); Content = root;
        SizeChanged += (_, _) => { EndSelectionDrag(true); DrawRegion(); };
        Deactivated += (_, _) => EndSelectionDrag(true);
        Closing += (_, _) => EndSelectionDrag(true);
        DrawRegion();
    }

    private void DrawRegion()
    {
        var rect = VideoAnalysis.PixelRegion(_region, _info);
        Canvas.SetLeft(_regionOutline, rect.X); Canvas.SetTop(_regionOutline, rect.Y);
        _regionOutline.Width = rect.Width; _regionOutline.Height = rect.Height;
        DrawSelectionHandles(rect);
        _coordinates.Text = $"  {rect.Width} × {rect.Height} 像素 · 左上 ({rect.X}, {rect.Y})";
    }
}
