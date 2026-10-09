using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuideMate.App;
using GuideMate.Core;

internal static class UpdateDialogChecks
{
    private const string Notes = """
        ## 本次更新

        - 新增**正式版本检查**能力：默认启动时检查更新，也可在设置页或系统托盘手动检查；查看版本说明后确认下载。
        - 新增独立更新器，可下载 Windows x64 完整 ZIP 并校验版本、文件清单和 GitHub Release 提供的 SHA-256 资产摘要，再执行覆盖更新。更新前会请求主程序正常保存并退出；文件替换失败时支持回滚。
        - 更新安装保留现有设置、全局热键、收藏、播放进度、窗口偏好、WebView2 登录资料和视觉缓存。
        - 新版 Release 使用完整 ZIP 和 GitHub 资产摘要；更新器继续兼容旧版本附带的 `.sha256` 校验文件。

        [查看完整发布说明](https://github.com/vibub/GuideMate/releases/tag/v1.0.3)
        """;
    private const string Formatting = """
        # Markdown 预览

        **粗体**、*斜体*和~~删除线~~，`settings.json`。

        - 保留资料
          - 热键和收藏
          - WebView2 登录资料
        - 保留进度

        3. 下载完整 ZIP
        4. 正常退出后安装

        > **提示**：更新前保存设置。

        ```text
        <literal> **保留代码原文**
        第二行
        ```

        | 项目 | 状态 |
        | --- | --- |
        | **热键** | 保留 |

        ---

        [发布说明](https://github.com/vibub/GuideMate/releases)
        [本地文件](file:///C:/Windows/System32/calc.exe)
        ![图片说明](https://example.test/image.png)

        &amp; &lt; &#x4E2D; <https://github.com/vibub/GuideMate>

        <script>alert('plain text')</script>
        """;

    public static void Run(string? screenshotDirectory)
    {
        var checks = 0;
        var owner = new Window { Width = 560, Height = 480, Left = 120, Top = 80, ShowInTaskbar = false, Title = "隔离更新弹窗检查" };
        new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        Window? dialog = null;
        try
        {
            dialog = Open("1.0.2", "v1.0.3", Notes);
            var labels = Descendants(dialog).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(labels.Contains("发现新版本 v1.0.3") && labels.Contains("当前版本 v1.0.2 → v1.0.3"), "both displayed versions use v");
            var viewer = Viewer(dialog); var document = viewer.Document;
            var elements = Descendants(document).OfType<TextElement>().ToArray();
            var plain = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Check(plain.Contains("本次更新") && !plain.Contains("## ") && !plain.Contains("**") && !plain.Contains("`.sha256`"), "Markdown syntax is rendered, not shown literally");
            Check(document.Blocks.OfType<Paragraph>().First().FontSize > document.FontSize, "heading has distinct typography");
            Check(elements.OfType<System.Windows.Documents.List>().Single().ListItems.Count == 4, "release bullets stay separate items");
            Check(elements.OfType<Span>().Any(span => span.FontWeight == FontWeights.Bold), "release emphasis is bold");
            Check(elements.OfType<Hyperlink>().Single().NavigateUri.Scheme == "https", "release link is clickable");
            Check(viewer.IsSelectionEnabled && !Descendants(dialog).OfType<TextBox>().Any(), "notes remain selectable in a document viewer");
            Check(Descendants(dialog).OfType<Button>().Single(button => button.ToolTip?.ToString() == "下载并更新").IsEnabled == false, "isolated install remains disabled");
            if (screenshotDirectory != null)
            {
                Directory.CreateDirectory(screenshotDirectory); owner.Show(); dialog.Show(); Pump();
                viewer.Selection.Select(document.ContentStart, document.ContentEnd);
                Check(viewer.Selection.Text.Contains("WebView2 登录资料"), "rendered notes support full text selection");
                viewer.Selection.Select(document.ContentStart, document.ContentStart);
                Capture(dialog, Path.Combine(screenshotDirectory, "update-dialog.png"));
                dialog.Width = 400; dialog.Height = 320; Pump();
                var scroll = Descendants(viewer, visual: true).OfType<ScrollViewer>().First();
                Check(viewer.ActualHeight > 30 && scroll.ScrollableHeight > 0, "small dialog gives long notes a scrollable viewport");
                scroll.ScrollToEnd(); Pump();
                Check(scroll.VerticalOffset > 0, "scroll reaches the last release note");
                var button = Descendants(dialog).OfType<Button>().Single(item => item.ToolTip?.ToString() == "下载并更新");
                var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Bottom <= dialog.ActualHeight && bounds.Right <= dialog.ActualWidth, "small dialog keeps update button visible");
                Capture(dialog, Path.Combine(screenshotDirectory, "update-dialog-small.png"));
            }
            dialog.Close(); dialog = Open("v1.0.3-rc.1", "1.0.3-rc.2", Formatting);
            Check(Descendants(dialog).OfType<TextBlock>().Any(text => text.Text == "当前版本 v1.0.3-rc.1 → v1.0.3-rc.2"), "existing prefix and prerelease labels are preserved");
            document = Viewer(dialog).Document; elements = Descendants(document).OfType<TextElement>().ToArray();
            Check(elements.OfType<Span>().Any(span => span.FontStyle == FontStyles.Italic) && elements.OfType<Span>().Any(span => span.TextDecorations == TextDecorations.Strikethrough), "italic and strikethrough render");
            Check(elements.OfType<System.Windows.Documents.List>().Count() == 3 && elements.OfType<System.Windows.Documents.List>().Single(list => list.MarkerStyle == TextMarkerStyle.Decimal).StartIndex == 3, "nested and numbered lists preserve structure and starting number");
            Check(elements.OfType<Section>().Any(), "quotes render as a distinct block");
            Check(elements.OfType<Run>().Any(run => run.Text.Contains("<literal> **保留代码原文**\n第二行")), "fenced code preserves literal markup and newlines");
            Check(document.Blocks.OfType<Table>().Single().RowGroups.Single().Rows.Count == 2, "Markdown table renders header and data");
            Check(elements.OfType<Hyperlink>().Count() == 2 && new TextRange(document.ContentStart, document.ContentEnd).Text.Contains("图片说明"), "non-web links and image descriptions stay text");
            Check(new TextRange(document.ContentStart, document.ContentEnd).Text.Contains("& < 中") && elements.OfType<Hyperlink>().Any(link => link.NavigateUri.AbsoluteUri == "https://github.com/vibub/GuideMate"), "entities and automatic links preserve content");
            Check(new TextRange(document.ContentStart, document.ContentEnd).Text.Contains("<script>alert('plain text')</script>"), "HTML stays literal text");
            if (screenshotDirectory != null) { dialog.Height = 680; dialog.Show(); Pump(); Capture(dialog, Path.Combine(screenshotDirectory, "update-markdown.png")); }
            dialog.Close(); dialog = Open("1.0.2", "v1.0.3", " \n ");
            document = Viewer(dialog).Document;
            Check(new TextRange(document.ContentStart, document.ContentEnd).Text.Trim() == "此版本未提供更新说明。", "empty notes retain fallback message");
        }
        finally { dialog?.Close(); owner.Close(); }
        Console.WriteLine($"Update dialog checks passed: {checks}");
        Window Open(string current, string target, string notes) => (Window)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("GuideMate.App.UpdateAvailableWindow")!, owner, current, new GitHubRelease(target, target, notes, false, false, []), false)!;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
    }

    private static FlowDocumentScrollViewer Viewer(Window window) => Descendants(window).OfType<FlowDocumentScrollViewer>().Single();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root, bool visual = false)
    {
        yield return root;
        var children = visual ? Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Select(i => VisualTreeHelper.GetChild(root, i)) : LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>();
        foreach (var child in children) foreach (var next in Descendants(child, visual)) yield return next;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
