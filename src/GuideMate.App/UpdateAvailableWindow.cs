namespace GuideMate.App;

internal sealed class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(Window owner, string currentVersion, GitHubRelease release, bool canInstall)
    {
        Owner = owner; Title = "随引有新版本"; Width = 530; Height = 450; MinWidth = 400; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.Canvas;
        var currentTag = "v" + currentVersion.TrimStart('v');
        var targetTag = "v" + release.Tag.TrimStart('v');
        var content = new DockPanel { Margin = new(24) };
        var heading = new StackPanel();
        heading.Children.Add(Ui.Text("发现新版本 " + targetTag, 23, Ui.Green));
        var version = Ui.Text("当前版本 " + currentTag + " → " + targetTag, 12, Ui.Muted);
        version.Margin = new(0, 10, 0, 12); heading.Children.Add(version);
        DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) };
        footer.Children.Add(Ui.Text(canInstall ? "下载校验完成后正常退出随引并安装，原有资料继续保留。" : "隔离测试模式可以检查版本，不能更新正式程序。", 12, Ui.Muted));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var later = Ui.Command("\uE711", "稍后", () => DialogResult = false); later.IsCancel = true;
        var update = Ui.Command("\uE896", "下载并更新", () => DialogResult = true, primary: true); update.IsEnabled = canInstall;
        buttons.Children.Add(later); buttons.Children.Add(update); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        var notes = new FlowDocumentScrollViewer
        {
            Document = ReleaseNotesDocument.Create(release.Notes), IsSelectionEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent, BorderThickness = new(0)
        };
        System.Windows.Automation.AutomationProperties.SetName(notes, "更新日志");
        content.Children.Add(new Border { Child = notes, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 227, 224)),
            BorderThickness = new(1), CornerRadius = new(6), Background = Brushes.White });
        Content = content;
    }
}
