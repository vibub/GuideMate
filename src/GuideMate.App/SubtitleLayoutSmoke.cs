namespace GuideMate.App;

public sealed partial class MainWindow
{
    private async Task VerifySubtitleLayoutAsync(List<string> checks)
    {
        var window = new SubtitleWindow { Owner = this, Topmost = false };
        var hint = new DirectionHint("西南 242°", 242, "攻略箭头 · 在线视觉", "synthetic layout example");
        const string caption = "沿着这条道路向西南走，到前面的山坡后向右转，再沿石壁继续前进。字幕应当使用浮窗的完整宽度，并在合适的位置自然换行。";
        try
        {
            window.Show();
            foreach (var width in new[] { 520, 260 })
            {
                window.Width = width;
                foreach (var (name, text, direction) in new[] {
                    ("direction", "", (DirectionHint?)hint),
                    ("subtitle", caption, (DirectionHint?)hint),
                    ("subtitle-only", caption, (DirectionHint?)null) })
                {
                    window.Update(text, direction); await Task.Delay(100);
                    window.VerifyLayoutAndCapture(Path.Combine(_dataPath, $"overlay-{name}-{width}.png"));
                    checks.Add($"floating {name} layout uses available width at {width} DIP");
                }
            }
            window.SizeToContent = SizeToContent.Manual; window.Height = 200;
            window.Update("", hint); await Task.Delay(100);
            window.VerifyLayoutAndCapture(Path.Combine(_dataPath, "overlay-after-resize.png"));
            checks.Add("floating height becomes compact after resize and subtitle clearing");
        }
        finally { window.Close(); }
    }
}
