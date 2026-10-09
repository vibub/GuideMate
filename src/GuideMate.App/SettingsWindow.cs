namespace GuideMate.App;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(MainWindow owner, AppSettings settings, NativeHotkeys keys, Func<Dictionary<string, string>, int, string?> apply)
    {
        Title = "随引设置"; Owner = owner; Width = 620; Height = 760; MinWidth = 400; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.Canvas;
        var sections = new StackPanel { Margin = new(22, 4, 22, 12) };
        var anchors = new List<(string Label, FrameworkElement Element)>();
        StackPanel Section(string title, string label)
        {
            var content = new StackPanel { Margin = new(18, 0, 18, 16) };
            content.Children.Add(Ui.Heading(title));
            var surface = new Border { Background = Brushes.White, CornerRadius = new(8), Margin = new(0, 0, 0, 14), Child = content };
            sections.Children.Add(surface);
            anchors.Add((label, surface));
            return content;
        }
        var panel = Section("全局热键", "热键");
        panel.Children.Add(Ui.Text("点击输入框录制快捷键，支持鼠标侧键及组合键。", 12, Ui.Muted));
        panel.Children.Add(Ui.Text("原神或 BetterGI 以管理员权限运行时，鼠标侧键可能无法响应。请从托盘退出随引，再右键原启动入口选择“以管理员身份运行”，原热键与登录资料会继续沿用。", 12, Ui.Muted));
        var names = new Dictionary<string, string> { ["PlayPause"] = "播放 / 暂停", ["SeekBack"] = "后退", ["SeekForward"] = "快进", ["RateUp"] = "加快倍速", ["RateDown"] = "降低倍速", ["TemporaryRate"] = "长按临时倍速", ["PreviousEpisode"] = "上一集", ["NextEpisode"] = "下一集", ["Immersive"] = "沉浸模式", ["FullscreenDanmaku"] = "全屏弹幕开关", ["Hide"] = "隐藏 / 恢复", ["ClickThrough"] = "鼠标穿透" };
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
        panel = Section("全屏弹幕", "弹幕");
        var danmaku = Ui.Toggle("B 站全屏弹幕", settings.FullscreenDanmaku, _ => { });
        System.Windows.Automation.AutomationProperties.SetName(danmaku, "B 站全屏弹幕");
        panel.Children.Add(danmaku);
        panel.Children.Add(Ui.Text("进入沉浸后铺满小窗所在屏幕，鼠标操作穿透。沿用 B 站弹幕开关与屏蔽结果。", 12, Ui.Muted));
        Slider SettingsSlider(string name, double min, double max, double value, double tick, Func<double, string> format)
        {
            var label = Ui.Text(name + "  " + format(value), 12);
            label.Margin = new(0, 10, 0, 0); panel.Children.Add(label);
            var slider = Ui.Slider(min, max, value, next => label.Text = name + "  " + format(next));
            slider.TickFrequency = tick; slider.IsSnapToTickEnabled = true;
            slider.SmallChange = tick; slider.LargeChange = tick;
            System.Windows.Automation.AutomationProperties.SetName(slider, name);
            panel.Children.Add(slider);
            return slider;
        }
        var danmakuArea = SettingsSlider("显示区域", 0.1, 1, settings.DanmakuDisplayArea, 0.1, value => $"{value:P0}");
        panel.Children.Add(Ui.Text("从屏幕顶部向下分配弹幕区域，底部固定弹幕也在此范围内。", 12, Ui.Muted));
        var danmakuOpacity = SettingsSlider("不透明度", 0, 1, settings.DanmakuOpacity, 0.05, value => $"{value:P0}");
        var danmakuFont = SettingsSlider("弹幕字号", 0.5, 2, settings.DanmakuFontScale, 0.1, value => $"{value:P0}");
        var danmakuSpeed = SettingsSlider("弹幕速度", 0.5, 2, settings.DanmakuSpeed, 0.25,
            value => value == 1 ? "适中（1x）" : $"{value:0.##}x");
        panel = Section("字幕与方向", "字幕");
        panel.Children.Add(Ui.Text("调整字幕与方向浮窗的文字大小，保存后生效。", 12, Ui.Muted));
        var subtitleOpacity = SettingsSlider("字幕窗口透明度", 0, 0.8, 1 - settings.SubtitleOpacity, 0.05, value => $"{value:P0}");
        var subtitleFont = SettingsSlider("字幕字号", 12, 48, settings.SubtitleFontSize, 1, value => $"{value:0}");
        var directionFont = SettingsSlider("方向指引字号", 12, 48, settings.DirectionFontSize, 1, value => $"{value:0}");
        panel = Section("沉浸小窗", "小窗");
        var hideFromAltTab = Ui.Toggle("在 Alt+Tab 中隐藏沉浸小窗", settings.HideImmersiveFromAltTab, _ => { });
        System.Windows.Automation.AutomationProperties.SetName(hideFromAltTab, "在 Alt+Tab 中隐藏沉浸小窗");
        panel.Children.Add(hideFromAltTab);
        panel.Children.Add(Ui.Text("隐藏时也移除任务栏按钮，可用托盘或“隐藏 / 恢复”热键找回。退出沉浸后恢复普通窗口入口。", 12, Ui.Muted));
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
        panel = Section("播放控制", "播放");
        panel.Children.Add(Ui.Text("跳转时长（秒）", 12));
        var seek = new TextBox { Text = settings.SeekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(seek);
        panel.Children.Add(Ui.Heading("临时倍速"));
        var temporaryRate = new ComboBox { Height = 36, ItemsSource = new[] { 0.5, 1, 1.5, 2, 2.5, 3, 4 }, SelectedItem = settings.TemporaryRate };
        if (temporaryRate.SelectedItem == null) temporaryRate.SelectedItem = 2d;
        panel.Children.Add(temporaryRate);
        panel.Children.Add(Ui.Heading("长按触发时间（毫秒）"));
        var holdTime = new TextBox { Text = settings.TemporaryHoldMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        panel.Children.Add(holdTime);
        panel = Section("浏览器缓存", "缓存");
        panel.Children.Add(Ui.Text("清理网页、图片和视频缓存，保留登录状态、保存的密码及网站设置。随引的热键、收藏、进度和窗口偏好不受影响。", 12, Ui.Muted));
        panel.Children.Add(Ui.Text("清理后部分网页资源需要重新下载；缓存会随继续浏览重新生成。", 12, Ui.Muted));
        var cacheStatus = Ui.Text(owner.WebViewCacheCleanupStatus, 12, Ui.Muted);
        cacheStatus.Margin = new(0, 10, 0, 8); panel.Children.Add(cacheStatus);
        Button clearCache = null!;
        clearCache = Ui.Command("\uE74D", "立即清理缓存（保留登录）", async () =>
        {
            clearCache.IsEnabled = false;
            cacheStatus.Text = "正在清理缓存…";
            try { cacheStatus.Text = await owner.ClearWebViewCacheAsync() + "\n" + owner.WebViewCacheCleanupStatus; }
            finally { clearCache.IsEnabled = true; }
        });
        panel.Children.Add(clearCache);
        var autoCleanCache = Ui.Toggle("定时自动清理缓存", settings.AutoCleanWebViewCache, _ => { });
        System.Windows.Automation.AutomationProperties.SetName(autoCleanCache, "定时自动清理缓存");
        autoCleanCache.Margin = new(0, 12, 0, 8); panel.Children.Add(autoCleanCache);
        panel.Children.Add(Ui.Text("清理周期", 12));
        var cleanupDays = new[] { 1, 3, 7, 30 };
        var cacheInterval = new ComboBox { Height = 36, IsEnabled = settings.AutoCleanWebViewCache };
        foreach (var days in cleanupDays)
            cacheInterval.Items.Add(new ComboBoxItem { Content = days switch { 1 => "每天", 3 => "每 3 天", 7 => "每周", _ => "每 30 天" }, Tag = days });
        // Preserve custom values already stored in a profile instead of silently replacing them.
        if (!cleanupDays.Contains(settings.WebViewCacheCleanupDays))
            cacheInterval.Items.Add(new ComboBoxItem { Content = $"每 {settings.WebViewCacheCleanupDays} 天", Tag = settings.WebViewCacheCleanupDays });
        cacheInterval.SelectedItem = cacheInterval.Items.Cast<ComboBoxItem>().Single(item => (int)item.Tag == settings.WebViewCacheCleanupDays);
        System.Windows.Automation.AutomationProperties.SetName(cacheInterval, "缓存清理周期");
        autoCleanCache.Checked += (_, _) => cacheInterval.IsEnabled = true;
        autoCleanCache.Unchecked += (_, _) => cacheInterval.IsEnabled = false;
        panel.Children.Add(cacheInterval);
        panel.Children.Add(Ui.Text("自动清理默认关闭，保存后生效。软件运行时到期清理，播放中延后至暂停或空闲；软件关闭期间的到期任务会在下次启动补做。立即清理会直接执行，取消设置不会撤销已清理的缓存。", 12, Ui.Muted));
        panel = Section("软件更新", "更新");
        panel.Children.Add(Ui.Text("当前版本  " + MainWindow.CurrentVersion, 13));
        var startupUpdates = Ui.Toggle("启动时检查更新", settings.CheckUpdatesOnStartup, _ => { });
        System.Windows.Automation.AutomationProperties.SetName(startupUpdates, "启动时检查更新");
        startupUpdates.Margin = new(0, 12, 0, 8); panel.Children.Add(startupUpdates);
        panel.Children.Add(Ui.Text("更新源：vibub/GuideMate 的 GitHub Releases。发现新版本时弹窗提醒，确认后下载并覆盖安装，保留现有资料。", 12, Ui.Muted));
        var checkUpdates = Ui.Command("\uE895", "检查更新", async () =>
        {
            await owner.CheckForUpdatesAsync(this);
        });
        panel.Children.Add(checkUpdates);
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
            settings.CheckUpdatesOnStartup = startupUpdates.IsChecked == true;
            WebViewCacheSchedule.Configure(settings, autoCleanCache.IsChecked == true,
                (int)((ComboBoxItem)cacheInterval.SelectedItem).Tag, DateTimeOffset.UtcNow);
            owner.ApplyWebViewCacheCleanupSettings();
            settings.TemporaryRate = (double)temporaryRate.SelectedItem;
            settings.FullscreenDanmaku = danmaku.IsChecked == true;
            settings.DanmakuDisplayArea = danmakuArea.Value;
            settings.DanmakuOpacity = danmakuOpacity.Value;
            settings.DanmakuFontScale = danmakuFont.Value;
            settings.DanmakuSpeed = danmakuSpeed.Value;
            owner.ApplyDanmakuSettings();
            settings.SubtitleOpacity = 1 - subtitleOpacity.Value;
            settings.SubtitleFontSize = subtitleFont.Value;
            settings.DirectionFontSize = directionFont.Value;
            owner.ApplySubtitleSettings();
            settings.HideImmersiveFromAltTab = hideFromAltTab.IsChecked == true;
            settings.ImmersiveOpacity = 1 - opacity.Value;
            settings.XRayEnabled = xray.IsChecked == true;
            settings.XRayRadius = radius.Value;
            owner.ApplySmallWindowSettings();
            owner.SaveSettings();
            Close();
        }, primary: true);
        save.HorizontalContentAlignment = HorizontalAlignment.Center;
        save.MinWidth = 120;
        var cancel = Ui.Command("\uE711", "取消", Close);
        cancel.IsCancel = true;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(save);
        var footer = new StackPanel { Margin = new(22, 0, 22, 16) };
        footer.Children.Add(error); footer.Children.Add(actions);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sections };
        var header = new StackPanel { Margin = new(24, 20, 24, 12) };
        var title = Ui.Text("偏好设置", 24); title.FontWeight = FontWeights.SemiBold;
        header.Children.Add(title);
        var description = Ui.Text("调整随引的操作习惯与观看体验，保存后生效。", 12, Ui.Muted);
        description.Margin = new(0, 6, 0, 12); header.Children.Add(description);
        var navigation = new WrapPanel();
        foreach (var anchor in anchors)
        {
            var target = anchor.Element;
            var button = new Button { Content = anchor.Label, Padding = new(14, 6, 14, 6), ToolTip = "转到" + anchor.Label + "设置" };
            System.Windows.Automation.AutomationProperties.SetName(button, "转到" + anchor.Label + "设置");
            button.Click += (_, _) => scroll.ScrollToVerticalOffset(target.TranslatePoint(new Point(), sections).Y);
            navigation.Children.Add(button);
        }
        header.Children.Add(navigation);
        var layout = new Grid { Background = Ui.Canvas };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new());
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(header);
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
    }
}
