using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace GuideMate.App;

public sealed partial class MainWindow : Window
{
    private const string MissingVideoNotice = "未找到可控制的 HTML5 视频。";
    private readonly string _dataPath;
    private readonly bool _isolated;
    private readonly string? _initialMedia;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly VisibleCursorWebView _browser = new();
    private readonly Grid _root = new();
    private readonly Grid _workspace = new();
    private readonly TextBox _address = new();
    private readonly TextBlock _status = Ui.Text("正在启动浏览器…", 11, Ui.Muted);
    private readonly TextBlock _clock = Ui.Text("00:00 / 00:00", 12, Ui.Muted);
    private readonly TextBlock _caption = Ui.Text("暂无字幕", 14);
    private readonly TextBlock _direction = Ui.Text("—", 32, Ui.Green);
    private readonly TextBlock _directionKind = Ui.Text("等待方向提示", 12, Ui.Muted);
    private readonly TextBlock _subtitleSource = Ui.Text("未加载字幕", 11, Ui.Muted);
    private readonly TextBlock _hotkeyStatus = Ui.Text("", 11, Ui.Muted);
    private readonly Slider _timeline = new() { Minimum = 0, Maximum = 1, Margin = new(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _rate = new() { Width = 76, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
    private readonly ComboBox _topmostMode = new() { Height = 36, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 4, 0, 4) };
    private readonly ListBox _bookmarks = new() { BorderThickness = new(0) };
    private readonly ListBox _history = new() { BorderThickness = new(0) };
    private Button _play = null!;
    private Button _minimizeButton = null!;
    private Button _hideToTrayButton = null!;
    private Thumb _dragHandle = null!;
    private TabControl _sidebar = null!;
    private readonly List<UIElement> _chrome = [];
    private Slider _volume = null!;
    private CheckBox _overlayToggle = null!;
    private CheckBox _throughToggle = null!;
    private CheckBox _holeToggle = null!;
    private Slider _opacitySlider = null!;
    private NativeHotkeys? _keys;
    private VideoRouter? _videoRouter;
    private bool _temporaryRateActive;
    private double? _temporaryRestoreRate;
    private string _mediaKey = "";
    private Forms.NotifyIcon? _tray;
    private SubtitleWindow? _overlay;
    private IReadOnlyList<SubtitleCue> _cues = [];
    private readonly HashSet<string> _combatEvents = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer _fadeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private double _position, _duration;
    private bool _paused = true, _syncing, _seeking, _immersive, _through, _hole, _closing, _faded;
    private string _url = "", _title = "随引", _lastCaption = "", _notice = "";
    private string? _localFile;
    private string? _pendingLocalFile;
    private Rect _normalBounds;
    private WindowState _normalWindowState;
    private WindowState _restoreWindowState = WindowState.Normal;
    private SavedVideo? _resume;
    private bool _resumeApplied;
    private bool _navigationRequested;
    private int _navigationVersion;

    public MainWindow(string dataPath, bool isolated = false, string? initialMedia = null)
    {
        _dataPath = dataPath; _isolated = isolated; _initialMedia = initialMedia;
        _store = new(dataPath); _settings = _store.Load();
        // Preserve the ICO decoder so WPF selects the matching frame for each system icon size.
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/guidemate.ico"));
        Title = "随引"; Width = Math.Clamp(_settings.Width, 860, 1800); Height = Math.Clamp(_settings.Height, 500, 1200);
        Left = _settings.Left; Top = _settings.Top; MinWidth = 860; MinHeight = 500;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize; Topmost = _settings.ShouldBeTopmost(_immersive); Opacity = _settings.Opacity;
        BuildUi();
        SourceInitialized += (_, _) =>
        {
            Ui.PlaceVisible(this); ApplyWindowSwitcher();
            _keys = new(this); _keys.Pressed += OnHotkey;
            _keys.TemporaryRateReleased += () => Fire(() => EndTemporaryRateAsync(true));
            var error = _keys.Apply(_settings.Hotkeys, _settings.TemporaryHoldMilliseconds);
            _hotkeyStatus.Text = (error ?? "全局热键已启用") + "\n紧急恢复：" + _keys.EmergencyBinding;
            if (!_keys.EmergencyAvailable) _hotkeyStatus.Text += $"（冲突 {_keys.EmergencyError}，穿透已禁用）";
            _throughToggle.IsEnabled = _keys.EmergencyAvailable;
            InitializeTray();
        };
        Loaded += async (_, _) => await InitializeBrowserAsync();
        Loaded += async (_, _) => await CheckUpdatesAtStartupAsync();
        SizeChanged += (_, _) => { UpdateClip(); UpdateSmallWindowLayout(); if (_onlineProfile != null) { InvalidateOnlineSample(); UpdateDirection(); } };
        LocationChanged += (_, _) => UpdateDanmakuOverlay();
        IsVisibleChanged += (_, _) => { UpdateDanmakuOverlay(); UpdateXRayTracking(); UpdateImmersiveControls(); if (!IsVisible) { InvalidateOnlineSample(); UpdateDirection(); } };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { CancelImmersiveDrag(); InvalidateOnlineSample(); UpdateDirection(); }
            else _restoreWindowState = WindowState;
            CancelEdgeSeek(); UpdateXRayTracking();
            UpdateOverlay(); UpdateDanmakuOverlay(); UpdateImmersiveControls();
        };
        _saveTimer.Tick += (_, _) => { UpdateHistory(); SaveSettings(); };
        _fadeTimer.Tick += (_, _) => { _fadeTimer.Stop(); _faded = false; ApplyWindowOpacity(); };
        _webViewCacheTimer.Tick += async (_, _) => await TryAutoCleanWebViewCacheAsync();
        Closing += (_, _) =>
        {
            _updateLifetime.Cancel();
            _webViewCacheTimer.Stop();
            CancelWindowResize(); CancelImmersiveDrag();
            CancelEdgeSeek(); StopXRayTracking();
            _closing = true; UpdateImmersiveControls(); _onlineVisionTimer.Stop(); InvalidateOnlineSample(); _saveTimer.Stop(); _fadeTimer.Stop(); UpdateHistory(); SaveSettings();
            _chromeServer?.Dispose();
            _keys?.Dispose(); _tray?.Icon?.Dispose(); _tray?.Dispose(); _overlay?.Close(); _danmakuOverlay?.Dispose(); _browser.Dispose();
        };
    }

    private void BuildUi()
    {
        _root.Background = Ui.Canvas;
        foreach (var height in new[] { new GridLength(46), new(44), new(1, GridUnitType.Star), new(54), new(28) })
            _root.RowDefinitions.Add(new() { Height = height });
        var title = new Grid { Background = Ui.Canvas };
        title.ColumnDefinitions.Add(new()); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(14, 0, 0, 0) };
        brand.Children.Add(new TextBlock { Text = "随引", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ui.Green, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "攻略跟随", Margin = new(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, FontSize = 12 });
        title.Children.Add(brand);
        title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleImmersive(); else if (e.OriginalSource is not Button && e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        var titleTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        titleTools.Children.Add(Ui.Icon("\uE713", "设置", OpenSettings));
        titleTools.Children.Add(Ui.Icon("\uE740", "沉浸模式", ToggleImmersive));
        _hideToTrayButton = Ui.Icon("\uE896", "隐藏到托盘", HideToTray);
        titleTools.Children.Add(_hideToTrayButton);
        _minimizeButton = Ui.Icon("\uE921", "最小化", () => WindowState = WindowState.Minimized);
        titleTools.Children.Add(_minimizeButton);
        titleTools.Children.Add(Ui.Icon("\uE8BB", "退出", Close));
        Grid.SetColumn(titleTools, 1); title.Children.Add(titleTools); _root.Children.Add(title);

        var navigation = new Grid { Margin = new(10, 4, 10, 4) };
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); navigation.ColumnDefinitions.Add(new()); navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var navTools = new StackPanel { Orientation = Orientation.Horizontal };
        navTools.Children.Add(Ui.Icon("\uE72B", "后退", () => { if (_browser.CoreWebView2?.CanGoBack == true) _browser.CoreWebView2.GoBack(); }));
        navTools.Children.Add(Ui.Icon("\uE72A", "前进", () => { if (_browser.CoreWebView2?.CanGoForward == true) _browser.CoreWebView2.GoForward(); }));
        navTools.Children.Add(Ui.Icon("\uE72C", "刷新", () => _browser.CoreWebView2?.Reload()));
        navigation.Children.Add(navTools);
        _address.Margin = new(6, 0, 6, 0); _address.ToolTip = "攻略网址";
        System.Windows.Automation.AutomationProperties.SetName(_address, "攻略网址");
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateInput(); e.Handled = true; } };
        Grid.SetColumn(_address, 1); navigation.Children.Add(_address);
        var rightNav = new StackPanel { Orientation = Orientation.Horizontal };
        rightNav.Children.Add(Ui.Icon("\uE8A7", "打开网址", NavigateInput));
        rightNav.Children.Add(Ui.Icon("\uE8B7", "打开本地视频", OpenMedia));
        rightNav.Children.Add(Ui.Icon("\uE734", "收藏当前进度", AddBookmark));
        rightNav.Children.Add(Ui.Icon("\uE774", "Chrome 登录同步", OpenChromeSync));
        Grid.SetColumn(rightNav, 2); navigation.Children.Add(rightNav);
        Grid.SetRow(navigation, 1); _root.Children.Add(navigation);

        _workspace.ColumnDefinitions.Add(new()); _workspace.ColumnDefinitions.Add(new() { Width = new(284) });
        _browser.DefaultBackgroundColor = System.Drawing.Color.Black;
        _videoSurface.Children.Add(_browser);
        _workspace.Children.Add(_videoSurface);
        var tabs = _sidebar = new TabControl { Padding = new(0), BorderThickness = new(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 224)), Background = Brushes.White };
        var follow = new StackPanel { Margin = new(18, 0, 18, 16) };
        follow.Children.Add(Ui.Heading("当前方向")); follow.Children.Add(_direction); follow.Children.Add(_directionKind);
        follow.Children.Add(Ui.Heading("同步字幕")); _caption.MinHeight = 40; follow.Children.Add(_caption);
        _subtitleSource.Margin = new(0, 6, 0, 4); follow.Children.Add(_subtitleSource);
        follow.Children.Add(Ui.Command("\uE8A5", "导入字幕", ImportSubtitles));
        follow.Children.Add(Ui.Command("\uE894", "清除导入字幕", () => { _cues = []; _lastCaption = ""; _subtitleSource.Text = "网页字幕"; }));
        _overlayToggle = Ui.Toggle("字幕与方向浮窗（仅沉浸模式）", _settings.SubtitleOverlay, value => { _settings.SubtitleOverlay = value; UpdateOverlay(); });
        follow.Children.Add(_overlayToggle);
        var offsetRow = new Grid(); offsetRow.ColumnDefinitions.Add(new()); offsetRow.ColumnDefinitions.Add(new() { Width = new(74) });
        offsetRow.Children.Add(Ui.Text("字幕延迟（秒）", 12));
        var offset = new TextBox { Text = _settings.SubtitleOffset.ToString(CultureInfo.InvariantCulture), ToolTip = "正值延后字幕，负值提前" };
        offset.LostFocus += (_, _) =>
        {
            if (double.TryParse(offset.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)) _settings.SubtitleOffset = Math.Clamp(value, -600, 600);
            offset.Text = _settings.SubtitleOffset.ToString(CultureInfo.InvariantCulture);
        };
        Grid.SetColumn(offset, 1); offsetRow.Children.Add(offset); follow.Children.Add(offsetRow);
        follow.Children.Add(Ui.Heading("窗口"));
        follow.Children.Add(Ui.Text("置顶模式", 11, Ui.Muted));
        _topmostMode.ItemsSource = new[] { "不置顶", "沉浸置顶", "始终置顶" };
        _topmostMode.SelectedIndex = (int)_settings.GetTopmostMode();
        System.Windows.Automation.AutomationProperties.SetName(_topmostMode, "置顶模式");
        _topmostMode.SelectionChanged += (_, _) =>
        {
            if (_topmostMode.SelectedIndex < 0) return;
            _settings.TopmostMode = (WindowTopmostMode)_topmostMode.SelectedIndex;
            _settings.Topmost = _settings.TopmostMode == WindowTopmostMode.Always;
            Topmost = _settings.ShouldBeTopmost(_immersive);
            SaveSettings();
        };
        follow.Children.Add(_topmostMode);
        _throughToggle = Ui.Toggle("鼠标穿透", false, SetClickThrough); follow.Children.Add(_throughToggle);
        _holeToggle = Ui.Toggle("中央透明孔", false, value => { _hole = value; UpdateClip(); }); follow.Children.Add(_holeToggle);
        follow.Children.Add(Ui.Text("透明孔大小", 11, Ui.Muted));
        follow.Children.Add(Ui.Slider(0.1, 0.65, _settings.HoleRatio, value => { _settings.HoleRatio = value; UpdateClip(); }));
        var opacityLabel = Ui.Text($"不透明度  {_settings.Opacity:P0}", 11, Ui.Muted); follow.Children.Add(opacityLabel);
        _opacitySlider = Ui.Slider(0.2, 1, _settings.Opacity, value => { _settings.Opacity = value; ApplyWindowOpacity(); opacityLabel.Text = $"不透明度  {value:P0}"; }); follow.Children.Add(_opacitySlider);
        follow.Children.Add(Ui.Heading("字幕触发"));
        follow.Children.Add(Ui.Toggle("战斗提示时暂停", _settings.PauseOnCombat, value => _settings.PauseOnCombat = value));
        follow.Children.Add(Ui.Toggle("战斗提示时短暂淡化", _settings.FadeOnCombat, value => _settings.FadeOnCombat = value));
        follow.Children.Add(Ui.Command("\uE768", "打开验证样例", OpenDemo));
        follow.Children.Add(_hotkeyStatus);
        tabs.Items.Add(new TabItem { Header = "跟随", Content = new ScrollViewer { Content = follow, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        tabs.Items.Add(new TabItem { Header = "视觉", Content = BuildVisionPanel() });
        tabs.Items.Add(new TabItem { Header = "收藏", Content = BuildLibrary(_bookmarks, true) });
        tabs.Items.Add(new TabItem { Header = "历史", Content = BuildLibrary(_history, false) });
        Grid.SetColumn(tabs, 1); _workspace.Children.Add(tabs);
        _dragHandle = new Thumb { Style = (Style)FindResource("ImmersiveDragHandle"), ToolTip = "拖动小窗；双击退出沉浸模式" };
        System.Windows.Automation.AutomationProperties.SetName(_dragHandle, "拖动小窗");
        _dragHandle.HorizontalAlignment = HorizontalAlignment.Left; _dragHandle.VerticalAlignment = VerticalAlignment.Top;
        _dragHandle.Margin = new(8); _dragHandle.Visibility = Visibility.Collapsed; _dragHandle.Opacity = 0.7;
        _dragHandle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_immersive || e.ClickCount != 2) return;
            e.Handled = true;
            ToggleImmersive();
        };
        _dragHandle.DragStarted += (_, e) => BeginImmersiveDrag(e);
        _dragHandle.DragCompleted += (_, e) => { FinishImmersiveDrag(e.Canceled); SampleImmersiveCursor(); };
        _workspace.Children.Add(_dragHandle);
        BuildSmallWindowControls(); BuildImmersiveFeedback();
        Grid.SetRow(_workspace, 2); _root.Children.Add(_workspace);

        var playback = new Grid { Margin = new(10, 4, 10, 4) };
        playback.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); playback.ColumnDefinitions.Add(new()); playback.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Ui.Icon("\uE892", "上一集", () => Fire(() => CommandAsync("previousEpisode"))));
        controls.Children.Add(Ui.Icon("\uE72B", "后退视频", () => Fire(() => CommandAsync("seek", -_settings.SeekSeconds))));
        _play = Ui.Icon("\uE768", "播放 / 暂停", () => Fire(() => CommandAsync("toggle")), primary: true); controls.Children.Add(_play);
        controls.Children.Add(Ui.Icon("\uE72A", "快进视频", () => Fire(() => CommandAsync("seek", _settings.SeekSeconds))));
        controls.Children.Add(Ui.Icon("\uE893", "下一集", () => Fire(() => CommandAsync("nextEpisode"))));
        playback.Children.Add(controls);
        _timeline.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        _timeline.PreviewMouseLeftButtonUp += (_, _) => { _seeking = false; Fire(() => CommandAsync("position", _timeline.Value)); };
        _timeline.PreviewKeyUp += (_, _) => Fire(() => CommandAsync("position", _timeline.Value));
        Grid.SetColumn(_timeline, 1); playback.Children.Add(_timeline);
        var extra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        extra.Children.Add(_clock);
        foreach (var rate in new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4 }) _rate.Items.Add(rate);
        _rate.SelectedItem = _settings.Rate;
        _rate.SelectionChanged += (_, _) => { if (!_syncing && _rate.SelectedItem is double rate) Fire(() => SetRateAsync(rate)); }; extra.Children.Add(_rate);
        extra.Children.Add(Ui.Icon("\uE74F", "静音 / 取消静音", () => Fire(() => CommandAsync("mute"))));
        _volume = Ui.Slider(0, 1, 0.75, value => { if (!_syncing) Fire(() => CommandAsync("volume", value)); });
        _volume.Width = 64; _volume.Margin = new(4, 0, 6, 0); extra.Children.Add(_volume);
        extra.Children.Add(Ui.Icon("\uE9A6", "聚焦网页视频", () => Fire(() => CommandAsync("focus"))));
        Grid.SetColumn(extra, 2); playback.Children.Add(extra);
        Grid.SetRow(playback, 3); _root.Children.Add(playback);
        var bottom = new Grid { Margin = new(14, 0, 14, 0) };
        bottom.Children.Add(_status); Grid.SetRow(bottom, 4); _root.Children.Add(bottom);
        _chrome.AddRange([title, navigation, playback, bottom]);
        BuildWindowResizeControls();
        Content = _root;
        RefreshLibraries();
    }

    private FrameworkElement BuildLibrary(ListBox list, bool bookmark)
    {
        var grid = new Grid { Margin = new(10) };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        list.ItemTemplate = new DataTemplate(typeof(SavedVideo));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(TextBlock.MarginProperty, new Thickness(4, 8, 4, 8));
        list.ItemTemplate.VisualTree = text;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is SavedVideo saved) NavigateSaved(saved); };
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Command("\uE768", "打开", () => { if (list.SelectedItem is SavedVideo saved) NavigateSaved(saved); }));
        buttons.Children.Add(Ui.Icon("\uE74D", "移除选中记录", () =>
        {
            if (list.SelectedItem is SavedVideo saved) { (bookmark ? _settings.Bookmarks : _settings.History).Remove(saved); RefreshLibraries(); SaveSettings(); }
        }));
        Grid.SetRow(buttons, 1); grid.Children.Add(buttons); return grid;
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_dataPath, "WebView2"));
            await _browser.EnsureCoreWebView2Async(environment);
            var core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping("guidemate.local", Path.Combine(AppContext.BaseDirectory, "assets"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping("frame.guidemate.local", Path.Combine(AppContext.BaseDirectory, "assets"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter("https://media.guidemate.local/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (_localFile == null || !Uri.TryCreate(core.Source, UriKind.Absolute, out var source) || source.Host != "guidemate.local"
                    || source.AbsolutePath != "/local.html" || new Uri(e.Request.Uri).AbsolutePath != "/video")
                { e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", ""); return; }
                if (e.Request.Method is not ("GET" or "HEAD"))
                { e.Response = environment.CreateWebResourceResponse(null, 405, "Method Not Allowed", "Allow: GET, HEAD"); return; }
                try { e.Response = MediaResponse.Create(environment, e.Request, _localFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                    _status.Text = _notice = "本地视频读取失败：" + ex.Message;
                }
            };
            var bridgeScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "bridge.js"));
            _videoRouter = new(core, bridgeScript);
            _videoRouter.MessageReceived += OnWebMessage;
            _videoRouter.ActiveChanged += () =>
            {
                ResetVideoAspectRatio(); ResetDanmaku(); ResetOnlineVision(); UpdateDirection();
                _keys?.CancelTemporaryRate(); Fire(EndTemporaryRateAsync);
                if (_immersive) Fire(() => CommandAsync("focusOn"));
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "about"))
                { e.Cancel = true; _status.Text = "仅支持 http/https 网页和应用本地播放器。"; return; }
                UpdateHistory();
                ResetVideoAspectRatio(); ResetDanmaku(); ResetOnlineVision();
                _keys?.CancelTemporaryRate();
                _temporaryRateActive = false; _temporaryRestoreRate = null; _mediaKey = "";
                var samePage = e.Uri == _url;
                if (!_navigationRequested) _resume = samePage ? CurrentVideo() : new SavedVideo { Rate = _settings.Rate };
                _navigationRequested = false; _resumeApplied = false;
                if (!samePage) { _cues = []; _combatEvents.Clear(); _subtitleSource.Text = "网页字幕"; }
                _position = 0; _duration = 0; _paused = true;
                CancelEdgeSeek(); UpdateEdgeProgress();
                _lastCaption = ""; _caption.Text = "暂无字幕"; _direction.Text = "—"; _directionKind.Text = "等待方向提示";
                _status.Text = "正在加载…"; _notice = "";
            };
            core.SourceChanged += (_, _) =>
            {
                _url = core.Source; _address.Text = DisplayAddress(_url);
                _localFile = _url.StartsWith("https://guidemate.local/local.html", StringComparison.OrdinalIgnoreCase) ? _pendingLocalFile : null;
                LoadVisualTrack();
            };
            core.DocumentTitleChanged += (_, _) => _title = core.DocumentTitle;
            core.NavigationCompleted += async (_, e) =>
            {
                _navigationVersion++;
                if (!e.IsSuccess) { _status.Text = "加载失败：" + e.WebErrorStatus; return; }
                _status.Text = "已加载";
                await core.ExecuteScriptAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "bridge.js")));
                if (core.Source.Contains("guidemate.local/demo")) LoadSubtitles(Path.Combine(AppContext.BaseDirectory, "assets", "demo.srt"));
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; Navigate(e.Uri); };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "bridge.js")));
            if (!_isolated) StartChromeBridge();
            WebViewCacheSchedule.Configure(_settings, _settings.AutoCleanWebViewCache,
                _settings.WebViewCacheCleanupDays, DateTimeOffset.UtcNow);
            ApplyWebViewCacheCleanupSettings();
            await TryAutoCleanWebViewCacheAsync();
            if (_closing) return;
            _saveTimer.Start(); UpdateOverlay();
            if (_initialMedia != null) NavigateMedia(_initialMedia);
            else Navigate("https://www.bilibili.com/");
            if (_store.LoadWarning != null) _status.Text = _store.LoadWarning;
        }
        catch (Exception ex)
        {
            _status.Text = "浏览器启动失败：" + ex.Message;
        }
    }

    private void OnWebMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            if (type.GetString() == "error") { _notice = root.GetProperty("message").GetString() ?? "播放失败"; _status.Text = _notice; return; }
            if (type.GetString() == "no-video")
            {
                ResetVideoAspectRatio(); ResetDanmaku(); InvalidateOnlineSample();
                _position = _duration = 0; _paused = true; _clock.Text = "00:00 / 00:00";
                _timeline.Value = 0; _timeline.Maximum = 1; ((TextBlock)_play.Content).Text = "\uE768";
                CancelEdgeSeek(); UpdateEdgeProgress();
                UpdateCaption(""); _status.Text = MissingVideoNotice; return;
            }
            if (type.GetString() != "state") return;
            if (_notice == MissingVideoNotice) _notice = "";
            var mediaKey = root.TryGetProperty("mediaKey", out var media) ? media.GetString() ?? "" : "";
            if (_mediaKey.Length > 0 && mediaKey != _mediaKey)
            {
                ResetVideoAspectRatio(); ResetDanmaku();
                _keys?.CancelTemporaryRate(); Fire(EndTemporaryRateAsync);
                _cues = []; _combatEvents.Clear(); _lastCaption = ""; _subtitleSource.Text = "网页字幕"; _notice = "";
                LoadVisualTrack();
            }
            if (_immersive && _mediaKey != mediaKey) Fire(() => CommandAsync("focusOn"));
            _mediaKey = mediaKey;
            _position = Finite(root, "time", 0); _duration = Finite(root, "duration", 0);
            _paused = root.GetProperty("paused").GetBoolean();
            UpdateVideoAspectRatio(root);
            UpdateOnlineVisionState(root);
            var rate = Math.Clamp(Finite(root, "rate", 1), 0.25, 4);
            SyncDanmaku(root, rate);
            if (!_temporaryRateActive && _temporaryRestoreRate is { } restoredRate && Math.Abs(rate - restoredRate) < 0.01) _temporaryRestoreRate = null;
            if (!_temporaryRateActive && _temporaryRestoreRate == null) _settings.Rate = rate;
            _syncing = true;
            _rate.SelectedItem = rate; _volume.Value = Math.Clamp(Finite(root, "volume", 0.75), 0, 1);
            if (!_seeking) { _timeline.Maximum = Math.Max(1, _duration); _timeline.Value = Math.Clamp(_position, 0, _timeline.Maximum); }
            ((TextBlock)_play.Content).Text = _paused ? "\uE768" : "\uE769";
            _syncing = false;
            _clock.Text = FormatTime(_position) + " / " + FormatTime(_duration);
            UpdateEdgeProgress();
            if (!_resumeApplied && _resume != null && _duration > 0)
            {
                _resumeApplied = true;
                Fire(async () => { await CommandAsync("position", _resume.Position); await SetRateAsync(_resume.Rate); });
            }
            var caption = _cues.Count > 0 ? Subtitles.At(_cues, _position, _settings.SubtitleOffset)
                : root.TryGetProperty("subtitle", out var subtitle) ? subtitle.GetString() ?? "" : "";
            UpdateCaption(caption);
            _status.Text = _notice.Length > 0 ? _notice : $"{(_paused ? "已暂停" : "播放中")} · {rate:0.##}x" + (_through ? " · 鼠标穿透" : "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { _syncing = false; }
    }

    private static double Finite(JsonElement root, string key, double fallback) => root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : fallback;
    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 360000)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");

    private void UpdateCaption(string caption)
    {
        if (caption != _lastCaption)
        {
            _lastCaption = caption;
            _caption.Text = caption.Length > 0 ? caption : "暂无字幕";
        }
        UpdateDirection();
        if (caption.Length == 0 || !DirectionAnalyzer.IsCombat(caption) || _paused || (!_settings.PauseOnCombat && !_settings.FadeOnCombat)) return;
        var eventKey = _cues.Count > 0 ? string.Join("|", _cues.Where(c => c.Start <= _position - _settings.SubtitleOffset && _position - _settings.SubtitleOffset < c.End).Select(c => c.Start.ToString("R", CultureInfo.InvariantCulture))) : caption;
        if (!_combatEvents.Add(eventKey)) return;
        if (_settings.PauseOnCombat) Fire(() => CommandAsync("pause"));
        if (_settings.FadeOnCombat) { _faded = true; ApplyWindowOpacity(); _fadeTimer.Stop(); _fadeTimer.Start(); }
    }

    private async Task<bool> CommandAsync(string action, double? value = null)
    {
        if (action is "position" or "seek" or "previousEpisode" or "nextEpisode")
        { InvalidateOnlineSample(); if (action is "position" or "seek") _videoSeeking = true; UpdateDirection(); }
        _notice = "";
        if (_browser.CoreWebView2 == null) { _status.Text = _notice = "浏览器尚未就绪。"; return false; }
        var request = JsonSerializer.Serialize(new { action, value });
        var response = await _videoRouter!.ExecuteAsync("window.guideMate?.command(" + request + ") ?? false", action is "previousEpisode" or "nextEpisode");
        if (response == "false" || response == "null") _status.Text = _notice = action is "previousEpisode" or "nextEpisode" ? "本页未找到可用的上一集 / 下一集。" : MissingVideoNotice;
        return response == "true";
    }
    private async Task<bool> SetRateAsync(double rate)
    {
        _keys?.CancelTemporaryRate();
        await EndTemporaryRateAsync();
        _settings.Rate = Math.Clamp(rate, 0.25, 4);
        _temporaryRestoreRate = _settings.Rate;
        return await CommandAsync("rate", _settings.Rate);
    }
    private async Task<bool> StartTemporaryRateAsync()
    {
        if (_temporaryRateActive || _duration <= 0) return false;
        _temporaryRestoreRate = _settings.Rate; _temporaryRateActive = true;
        return await CommandAsync("rate", _settings.TemporaryRate);
    }
    private Task EndTemporaryRateAsync() => EndTemporaryRateAsync(false);
    private async Task EndTemporaryRateAsync(bool feedback)
    {
        if (!_temporaryRateActive) return;
        _temporaryRateActive = false;
        var restoredRate = _temporaryRestoreRate ?? _settings.Rate;
        var version = _temporaryFeedbackVersion;
        var success = await CommandAsync("rate", restoredRate);
        if (feedback && success) ShowHotkeyFeedback("恢复倍速" + FeedbackNumber(restoredRate) + "x", version);
    }
    private async void Fire(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) { if (!_closing) _status.Text = "操作失败：" + ex.Message; }
    }

    private void NavigateInput()
    {
        var text = _address.Text.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        Navigate(text);
    }
    private void Navigate(string url, SavedVideo? resume = null)
    {
        if (_browser.CoreWebView2 == null) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        { _status.Text = "请输入有效的 http/https 地址。"; return; }
        _resume = resume ?? new SavedVideo { Rate = _settings.Rate };
        ResetDanmaku(); ResetOnlineVision(); UpdateDirection();
        if (!uri.AbsoluteUri.StartsWith("https://guidemate.local/local.html", StringComparison.OrdinalIgnoreCase)) _pendingLocalFile = null;
        _resumeApplied = false;
        _navigationRequested = true;
        _browser.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    private static string DisplayAddress(string url) => url.Contains("guidemate.local/demo") ? "https://guidemate.local/demo.html" : url;
    private void OpenDemo() => Navigate("https://guidemate.local/demo.html");
    private void OpenMedia()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "视频文件|*.mp4;*.webm;*.mkv;*.mov;*.avi|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true || _browser.CoreWebView2 == null) return;
        NavigateMedia(dialog.FileName);
    }
    private void NavigateSaved(SavedVideo saved)
    {
        if (saved.LocalPath != null) NavigateMedia(saved.LocalPath, saved);
        else Navigate(saved.Url, saved);
    }
    private void NavigateMedia(string path, SavedVideo? resume = null)
    {
        if (!File.Exists(path)) { _status.Text = _notice = "本地视频已移动或不存在。"; return; }
        _pendingLocalFile = Path.GetFullPath(path);
        Navigate("https://guidemate.local/local.html?file=" + Uri.EscapeDataString(Path.GetFileName(_pendingLocalFile)), resume);
    }
    private void ImportSubtitles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "字幕文件|*.srt;*.vtt;*.json|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) LoadSubtitles(dialog.FileName);
    }
    private void LoadSubtitles(string path)
    {
        try
        {
            var cues = Subtitles.Parse(File.ReadAllText(path), Path.GetExtension(path));
            _cues = cues; _lastCaption = ""; _combatEvents.Clear();
            _subtitleSource.Text = Path.GetFileName(path) + $" · {cues.Count} 条";
            UpdateCaption(Subtitles.At(cues, _position, _settings.SubtitleOffset));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException)
        { _status.Text = _notice = "字幕导入失败：" + ex.Message; }
    }

    private void AddBookmark()
    {
        if (_url.Length == 0) return;
        _settings.Bookmarks.RemoveAll(x => x.Url == _url);
        _settings.Bookmarks.Insert(0, CurrentVideo()); RefreshLibraries(); SaveSettings(); _status.Text = "已收藏当前播放进度。";
    }
    private SavedVideo CurrentVideo() => new() { Url = _url, LocalPath = _localFile, Title = _title, Position = _position, Rate = _settings.Rate, UpdatedAt = DateTime.Now };
    private void UpdateHistory()
    {
        if (_url.Length == 0) return;
        _settings.History.RemoveAll(x => x.Url == _url);
        _settings.History.Insert(0, CurrentVideo());
        if (_settings.History.Count > 80) _settings.History.RemoveRange(80, _settings.History.Count - 80);
        RefreshLibraries();
    }
    private void RefreshLibraries()
    {
        _bookmarks.ItemsSource = null; _bookmarks.ItemsSource = _settings.Bookmarks;
        _history.ItemsSource = null; _history.ItemsSource = _settings.History;
    }
    public void SaveSettings()
    {
        if (!IsLoaded) return;
        var rect = _immersive ? _normalBounds : WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        _settings.Left = rect.Left; _settings.Top = rect.Top; _settings.Width = rect.Width; _settings.Height = rect.Height;
        if (_immersive) RememberImmersiveBounds();
        if (_overlay != null) RememberOverlayBounds();
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "设置保存失败：" + ex.Message; }
    }

    private void OpenSettings()
    {
        if (_keys == null) return;
        try
        {
            new SettingsWindow(this, _settings, _keys, (bindings, holdMilliseconds) =>
            {
                var error = _keys.Apply(bindings, holdMilliseconds);
                if (error == null) { _settings.Hotkeys = bindings; _settings.TemporaryHoldMilliseconds = holdMilliseconds; _hotkeyStatus.Text = "全局热键已启用\n紧急恢复：" + _keys.EmergencyBinding; }
                return error;
            }).ShowDialog();
        }
        finally
        {
            if (!_closing)
            {
                var restoreError = _keys.ResumeOrdinaryBindings();
                if (restoreError != null) _hotkeyStatus.Text = restoreError + "\n紧急恢复：" + _keys.EmergencyBinding;
            }
        }
    }
    private void OnHotkey(string action)
    {
        ClearHotkeyFeedback();
        var version = _hotkeyFeedbackVersion;
        switch (action)
        {
            case "PlayPause": Fire(() => VideoHotkeyAsync("toggle", null, "", version)); break;
            case "SeekBack": Fire(() => VideoHotkeyAsync("seek", -_settings.SeekSeconds, "后退" + FeedbackNumber(_settings.SeekSeconds) + "s", version)); break;
            case "SeekForward": Fire(() => VideoHotkeyAsync("seek", _settings.SeekSeconds, "快进" + FeedbackNumber(_settings.SeekSeconds) + "s", version)); break;
            case "RateUp": Fire(() => RateHotkeyAsync(_settings.Rate + 0.25, version)); break;
            case "RateDown": Fire(() => RateHotkeyAsync(_settings.Rate - 0.25, version)); break;
            case "TemporaryRate": Fire(() => TemporaryRateHotkeyAsync(version)); break;
            case "PreviousEpisode": Fire(() => VideoHotkeyAsync("previousEpisode", null, "上一集", version)); break;
            case "NextEpisode": Fire(() => VideoHotkeyAsync("nextEpisode", null, "下一集", version)); break;
            case "Immersive": ToggleImmersive(); ShowHotkeyFeedback("沉浸模式", _hotkeyFeedbackVersion); break;
            case "FullscreenDanmaku":
                _settings.FullscreenDanmaku = !_settings.FullscreenDanmaku;
                ApplyDanmakuSettings(); SaveSettings();
                ShowHotkeyFeedback(_settings.FullscreenDanmaku ? "全屏弹幕：开" : "全屏弹幕：关", version);
                break;
            case "Hide": ToggleHidden(); ShowHotkeyFeedback("已恢复", _hotkeyFeedbackVersion); break;
            case "ClickThrough":
                var through = !_through;
                SetClickThrough(through);
                ShowHotkeyFeedback(_through == through ? through ? "开启鼠标穿透" : "关闭鼠标穿透" : "鼠标穿透不可用", version);
                break;
            case "Emergency": EmergencyRestore(); break;
        }
    }
    private void SetClickThrough(bool enabled)
    {
        if (enabled && _keys?.EmergencyAvailable != true) { _throughToggle.IsChecked = false; _status.Text = "紧急恢复热键不可用，无法启用穿透。"; return; }
        if (_through == enabled) return;
        CancelWindowResize();
        _through = enabled; NativeHotkeys.ClickThrough(this, enabled);
        if (_overlay?.IsVisible == true) NativeHotkeys.ClickThrough(_overlay, enabled);
        _throughToggle.IsChecked = enabled;
    }
    private void ToggleImmersive()
    {
        CancelWindowResize(); CancelImmersiveDrag();
        CancelEdgeSeek(); StopXRayTracking();
        // Refresh the WPF composition surface after changing a layered window's layout.
        _browser.Visibility = Visibility.Hidden;
        if (_immersive) RememberImmersiveBounds();
        else
        {
            _normalBounds = WindowState == WindowState.Normal ? new(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            _normalWindowState = WindowState == WindowState.Minimized ? _restoreWindowState : WindowState;
        }
        WindowState = WindowState.Normal;
        _immersive = !_immersive;
        ResizeMode = _immersive ? ResizeMode.NoResize : ResizeMode.CanResize;
        _root.Background = _immersive ? Brushes.Transparent : Ui.Canvas;
        _edgeProgress.Visibility = _immersive ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowOpacity();
        Topmost = _settings.ShouldBeTopmost(_immersive);
        _workspace.Cursor = Cursors.Arrow;
        _workspace.ForceCursor = _immersive;
        foreach (var element in _chrome) element.Visibility = _immersive ? Visibility.Collapsed : Visibility.Visible;
        _sidebar.Visibility = _immersive ? Visibility.Collapsed : Visibility.Visible;
        _dragHandle.Visibility = _immersive ? Visibility.Hidden : Visibility.Collapsed;
        if (_immersive)
        {
            MinWidth = 320; MinHeight = 180;
            foreach (var row in new[] { 0, 1, 3, 4 }) _root.RowDefinitions[row].Height = new(0);
            _workspace.ColumnDefinitions[1].Width = new(0);
            var bounds = _settings.ImmersiveBounds ?? new WindowPlacement(Left, Top, 640, 360);
            Width = Math.Max(MinWidth, bounds.Width); Height = Math.Max(MinHeight, bounds.Height);
            Left = bounds.Left; Top = bounds.Top;
            ApplyImmersiveAspectRatio();
            Ui.PlaceVisible(this);
            Fire(() => CommandAsync("focusOn"));
        }
        else
        {
            MinWidth = 860; MinHeight = 500;
            _root.RowDefinitions[0].Height = new(46); _root.RowDefinitions[1].Height = new(44);
            _root.RowDefinitions[3].Height = new(54); _root.RowDefinitions[4].Height = new(28);
            _workspace.ColumnDefinitions[1].Width = new(284);
            Width = _normalBounds.Width; Height = _normalBounds.Height; Left = _normalBounds.Left; Top = _normalBounds.Top;
            WindowState = _normalWindowState;
            Fire(() => CommandAsync("focusOff"));
        }
        ApplyWindowSwitcher(); UpdateClip(); UpdateOverlay(); UpdateDanmakuOverlay(); UpdateImmersiveControls();
        SaveSettings();
        Dispatcher.InvokeAsync(() => { _browser.Visibility = Visibility.Visible; _browser.UpdateLayout(); UpdateSmallWindowLayout(); UpdateXRayTracking(); }, DispatcherPriority.Loaded);
    }
    private void ToggleHidden()
    {
        if (IsVisible && WindowState != WindowState.Minimized) HideToTray();
        else RestoreMainWindow();
    }
    private void HideToTray()
    {
        CancelImmersiveDrag();
        CancelEdgeSeek();
        Hide(); _overlay?.Hide();
    }
    private void RestoreMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreWindowState;
        UpdateOverlay();
        if (!_through) Activate();
    }
    private void EmergencyRestore()
    {
        _keys?.CancelTemporaryRate(); Fire(EndTemporaryRateAsync);
        SetClickThrough(false); _hole = false; _holeToggle.IsChecked = false;
        if (_immersive) ToggleImmersive();
        _faded = false; _fadeTimer.Stop(); _opacitySlider.Value = 1; Opacity = 1;
        UpdateClip(); RestoreMainWindow();
    }
    private void UpdateClip()
    {
        if (!_hole || _root.ActualWidth <= 0) { _root.Clip = null; return; }
        var width = _root.ActualWidth; var height = _root.ActualHeight;
        var holeWidth = width * _settings.HoleRatio; var holeHeight = height * _settings.HoleRatio;
        _root.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, width, height)),
            new RectangleGeometry(new Rect((width - holeWidth) / 2, (height - holeHeight) / 2, holeWidth, holeHeight)));
    }
    private void RememberImmersiveBounds()
    {
        var rect = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        var bounds = new WindowPlacement(rect.Left, rect.Top, rect.Width, rect.Height);
        if (bounds.IsValid) _settings.ImmersiveBounds = bounds;
    }
    private void RememberOverlayBounds()
    {
        if (_overlay == null) return;
        _settings.OverlayLeft = _overlay.Left; _settings.OverlayTop = _overlay.Top; _settings.OverlayWidth = _overlay.Width;
    }

    internal void ApplySubtitleSettings() => _overlay?.ApplySettings(_settings);

    private void UpdateOverlay()
    {
        if (!_immersive || !_settings.SubtitleOverlay || !IsVisible || WindowState == WindowState.Minimized) { _overlay?.Hide(); return; }
        if (_overlay == null)
        {
            _overlay = new() { Left = _settings.OverlayLeft, Top = _settings.OverlayTop, Width = Math.Max(260, _settings.OverlayWidth) };
            ApplySubtitleSettings();
            _overlay.PlacementChanged += SaveSettings;
            _overlay.Closing += (_, _) => SaveSettings();
            _overlay.Show(); Ui.PlaceVisible(_overlay);
            _overlay.Closed += (_, _) => { _overlay = null; if (!_closing) { _settings.SubtitleOverlay = false; _overlayToggle.IsChecked = false; } };
        }
        else _overlay.Show();
        _overlay.Update(_lastCaption, CurrentDirection());
        NativeHotkeys.ClickThrough(_overlay, _through);
    }
    private void InitializeTray()
    {
        _tray = new Forms.NotifyIcon { Text = "随引", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("恢复窗口", null, (_, _) => Dispatcher.Invoke(EmergencyRestore));
        menu.Items.Add("播放 / 暂停", null, (_, _) => Dispatcher.Invoke(() => Fire(() => CommandAsync("toggle"))));
        menu.Items.Add("检查更新", null, (_, _) => Dispatcher.Invoke(async () => await CheckForUpdatesAsync()));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(EmergencyRestore);
    }

}
