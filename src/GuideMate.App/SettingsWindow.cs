namespace GuideMate.App;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(MainWindow owner, AppSettings settings, NativeHotkeys keys, Func<Dictionary<string, string>, int, string?> apply)
    {
        Title = "随引设置"; Owner = owner; Width = 480; Height = 680; MinWidth = 400; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var panel = new StackPanel { Margin = new(22) };
        panel.Children.Add(Ui.Heading("全局热键"));
        var names = new Dictionary<string, string> { ["PlayPause"] = "播放 / 暂停", ["SeekBack"] = "后退", ["SeekForward"] = "快进", ["RateUp"] = "加快倍速", ["RateDown"] = "降低倍速", ["TemporaryRate"] = "长按临时倍速", ["PreviousEpisode"] = "上一集", ["NextEpisode"] = "下一集", ["Immersive"] = "沉浸模式", ["Hide"] = "隐藏 / 恢复", ["ClickThrough"] = "鼠标穿透" };
        var fields = new Dictionary<string, TextBox>();
        var error = Ui.Text("", 12, Brushes.Firebrick); error.Margin = new(0, 10, 0, 8);
        void RecordingChanged(bool recording)
        {
            if (recording) keys.SuspendOrdinaryBindings();
            else
            {
                var restoreError = keys.ResumeOrdinaryBindings();
                if (restoreError != null) error.Text = restoreError;
            }
        }
        foreach (var pair in names)
        {
            var row = new Grid { Margin = new(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new() { Width = new(130) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(Ui.Text(pair.Value));
            var field = new HotkeyRecorder(settings.Hotkeys.GetValueOrDefault(pair.Key, AppSettings.DefaultHotkeys()[pair.Key]), RecordingChanged, message => error.Text = message);
            System.Windows.Automation.AutomationProperties.SetName(field, pair.Value + "热键");
            Grid.SetColumn(field, 1); row.Children.Add(field); fields[pair.Key] = field; panel.Children.Add(row);
        }
        panel.Children.Add(Ui.Text("紧急恢复：" + keys.EmergencyBinding, 12, Ui.Green));
        panel.Children.Add(Ui.Heading("沉浸小窗"));
        var opacityLabel = Ui.Text($"小窗透明度  {1 - settings.GetImmersiveOpacity():P0}", 12);
        panel.Children.Add(opacityLabel);
        var opacity = Ui.Slider(0, 0.8, 1 - settings.GetImmersiveOpacity(), value => opacityLabel.Text = $"小窗透明度  {value:P0}");
        opacity.TickFrequency = 0.05; opacity.IsSnapToTickEnabled = true;
        System.Windows.Automation.AutomationProperties.SetName(opacity, "小窗透明度");
        panel.Children.Add(opacity);
        var xray = Ui.Toggle("鼠标 X 光", settings.XRayEnabled, _ => { });
        System.Windows.Automation.AutomationProperties.SetName(xray, "鼠标 X 光");
        panel.Children.Add(xray);
        var radiusLabel = Ui.Text($"X 光半径  {settings.XRayRadius:0}", 12);
        panel.Children.Add(radiusLabel);
        var radius = Ui.Slider(20, 200, settings.XRayRadius, value => radiusLabel.Text = $"X 光半径  {value:0}");
        radius.TickFrequency = 5; radius.IsSnapToTickEnabled = true; radius.IsEnabled = settings.XRayEnabled;
        System.Windows.Automation.AutomationProperties.SetName(radius, "X 光半径");
        xray.Checked += (_, _) => radius.IsEnabled = true;
        xray.Unchecked += (_, _) => radius.IsEnabled = false;
        panel.Children.Add(radius);
        panel.Children.Add(Ui.Heading("跳转时长（秒）"));
        var seek = new TextBox { Text = settings.SeekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(seek);
        panel.Children.Add(Ui.Heading("临时倍速"));
        var temporaryRate = new ComboBox { Height = 30, ItemsSource = new[] { 0.5, 1, 1.5, 2, 2.5, 3, 4 }, SelectedItem = settings.TemporaryRate };
        if (temporaryRate.SelectedItem == null) temporaryRate.SelectedItem = 2d;
        panel.Children.Add(temporaryRate);
        panel.Children.Add(Ui.Heading("长按触发时间（毫秒）"));
        var holdTime = new TextBox { Text = settings.TemporaryHoldMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(holdTime);
        var save = Ui.Command("\uE74E", "保存设置", () =>
        {
            var restoreError = keys.ResumeOrdinaryBindings();
            if (restoreError != null) { error.Text = restoreError; return; }
            if (!double.TryParse(seek.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 1 || seconds > 120)
            { error.Text = "跳转时长应为 1 至 120 秒。"; return; }
            if (!int.TryParse(holdTime.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var holdMilliseconds) || holdMilliseconds is < 100 or > 2000)
            { error.Text = "长按触发时间应为 100 至 2000 毫秒。"; return; }
            var result = apply(fields.ToDictionary(p => p.Key, p => p.Value.Text.Trim()), holdMilliseconds);
            if (result != null) { error.Text = result; return; }
            settings.SeekSeconds = seconds;
            settings.TemporaryRate = (double)temporaryRate.SelectedItem;
            settings.ImmersiveOpacity = 1 - opacity.Value;
            settings.XRayEnabled = xray.IsChecked == true;
            settings.XRayRadius = radius.Value;
            owner.ApplySmallWindowSettings();
            owner.SaveSettings();
            Close();
        });
        var footer = new StackPanel { Margin = new(22, 0, 22, 16) };
        footer.Children.Add(error); footer.Children.Add(save);
        var layout = new Grid(); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel });
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
    }
}
