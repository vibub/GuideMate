using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GuideMate.App;

/// <summary>独立框选完整小地图；取消不会修改用户配置。</summary>
internal sealed class MapRegionWindow : Window
{
    public ArrowRegion? Result { get; private set; }
    public MapRegionWindow(Window owner, byte[] image, string title, ArrowRegion? previous)
    {
        Owner = owner; Title = title; Width = 1000; Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(image); bitmap.EndInit(); bitmap.Freeze();
        var canvas = new Canvas { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Background = Brushes.Transparent };
        var picture = new Grid { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight };
        picture.Children.Add(new Image { Source = bitmap }); picture.Children.Add(canvas);
        var outline = new Rectangle { Stroke = Brushes.OrangeRed, StrokeThickness = 3, IsHitTestVisible = false };
        canvas.Children.Add(outline);
        ArrowRegion? selected = previous is { IsValid: true } ? previous : null;
        Point? origin = null;
        void Draw()
        {
            if (selected == null) return;
            Canvas.SetLeft(outline, selected.X * bitmap.PixelWidth); Canvas.SetTop(outline, selected.Y * bitmap.PixelHeight);
            outline.Width = selected.Width * bitmap.PixelWidth; outline.Height = selected.Height * bitmap.PixelHeight;
        }
        canvas.MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(canvas); canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) =>
        {
            if (origin is not { } start) return;
            var end = e.GetPosition(canvas);
            var x = Math.Clamp(end.X, 0, bitmap.PixelWidth); var y = Math.Clamp(end.Y, 0, bitmap.PixelHeight);
            var size = Math.Min(Math.Abs(x - start.X), Math.Abs(y - start.Y));
            if (size < 8) return;
            selected = new((x < start.X ? start.X - size : start.X) / bitmap.PixelWidth,
                (y < start.Y ? start.Y - size : start.Y) / bitmap.PixelHeight, size / bitmap.PixelWidth, size / bitmap.PixelHeight);
            Draw();
        };
        canvas.MouseLeftButtonUp += (_, _) => { origin = null; canvas.ReleaseMouseCapture(); };
        canvas.LostMouseCapture += (_, _) => origin = null;
        var root = new DockPanel { Margin = new(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.Command("\uE74E", "保存选区", () =>
        {
            if (selected is not { IsValid: true } || selected.Width * bitmap.PixelWidth < 96)
            { MessageBox.Show(this, "请框选完整小地图，原始宽度至少 96 像素。", "地图校准"); return; }
            Result = selected; DialogResult = true;
        }));
        actions.Children.Add(Ui.Command("\uE711", "取消", () => DialogResult = false));
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var help = Ui.Text("拖动框选整个圆形小地图的外接正方形，玩家应位于中心。首版要求地图北向固定、地表区域一致。", 14);
        help.TextWrapping = TextWrapping.Wrap; DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        root.Children.Add(new Viewbox { Child = picture, Stretch = Stretch.Uniform }); Content = root; Draw();
    }
}
