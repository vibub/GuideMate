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
    private readonly bool _smoke;
    private readonly string? _initialMedia;
    private readonly string? _profileProbe;
    private readonly string? _episodeProbe;
    private readonly bool _smallWindowProbe;
    private readonly bool _onlineVisionProbe;
    private readonly string? _onlineVisionLiveUrl;
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
    private readonly ComboBox _rate = new() { Width = 76, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
    private readonly ComboBox _topmostMode = new() { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 4, 0, 4) };
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
    private WindowState _restoreWindowState = WindowState.Normal;
    private SavedVideo? _resume;
    private bool _resumeApplied;
    private bool _navigationRequested;
    private int _navigationVersion;

    public MainWindow(string dataPath, bool smoke, string? initialMedia = null, string? profileProbe = null, string? episodeProbe = null, bool smallWindowProbe = false, bool onlineVisionProbe = false, string? onlineVisionLiveUrl = null, bool danmakuProbe = false, string? danmakuLiveUrl = null)
    {
        _dataPath = dataPath; _smoke = smoke; _initialMedia = initialMedia; _profileProbe = profileProbe; _episodeProbe = episodeProbe;
        _smallWindowProbe = smallWindowProbe;
        _onlineVisionProbe = onlineVisionProbe;
        _onlineVisionLiveUrl = onlineVisionLiveUrl;
        _danmakuProbe = danmakuProbe; _danmakuLiveUrl = danmakuLiveUrl;
        _store = new(dataPath); _settings = _store.Load();
        Title = "随引"; Width = Math.Clamp(_settings.Width, 860, 1800); Height = Math.Clamp(_settings.Height, 500, 1200);
        Left = _settings.Left; Top = _settings.Top; MinWidth = 860; MinHeight = 500;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = _settings.ShouldBeTopmost(_immersive); Opacity = _settings.Opacity;
        BuildUi();
        SourceInitialized += (_, _) =>
        {
            Ui.PlaceVisible(this);
            _keys = new(this); _keys.Pressed += OnHotkey;
            _keys.TemporaryRateReleased += () => Fire(EndTemporaryRateAsync);
            var error = _keys.Apply(_settings.Hotkeys, _settings.TemporaryHoldMilliseconds);
            _hotkeyStatus.Text = (error ?? "全局热键已启用") + "\n紧急恢复：" + _keys.EmergencyBinding;
            if (!_keys.EmergencyAvailable) _hotkeyStatus.Text += $"（冲突 {_keys.EmergencyError}，穿透已禁用）";
            _throughToggle.IsEnabled = _keys.EmergencyAvailable;
            InitializeTray();
        };
        Loaded += async (_, _) => await InitializeBrowserAsync();
        SizeChanged += (_, _) => { UpdateClip(); UpdateSmallWindowLayout(); if (_onlineProfile != null) { InvalidateOnlineSample(); UpdateDirection(); } };
        LocationChanged += (_, _) => UpdateDanmakuOverlay();
        IsVisibleChanged += (_, _) => { UpdateDanmakuOverlay(); UpdateXRayTracking(); if (!IsVisible) { InvalidateOnlineSample(); UpdateDirection(); } };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { CancelImmersiveDrag(); InvalidateOnlineSample(); UpdateDirection(); }
            else _restoreWindowState = WindowState;
            CancelEdgeSeek(); UpdateXRayTracking();
            UpdateOverlay(); UpdateDanmakuOverlay();
        };
        _saveTimer.Tick += (_, _) => { UpdateHistory(); SaveSettings(); };
        _fadeTimer.Tick += (_, _) => { _fadeTimer.Stop(); _faded = false; ApplyWindowOpacity(); };
        Closing += (_, _) =>
        {
            CancelImmersiveDrag();
            CancelEdgeSeek(); StopXRayTracking();
            _closing = true; _onlineVisionTimer.Stop(); InvalidateOnlineSample(); _saveTimer.Stop(); _fadeTimer.Stop(); UpdateHistory(); SaveSettings();
            _chromeServer?.Dispose();
            _keys?.Dispose(); _tray?.Dispose(); _overlay?.Close(); _danmakuOverlay?.Close(); _browser.Dispose();
        };
    }

    private void BuildUi()
    {
        _root.Background = Brushes.White;
        foreach (var height in new[] { new GridLength(46), new(44), new(1, GridUnitType.Star), new(54), new(28) })
            _root.RowDefinitions.Add(new() { Height = height });
        var title = new Grid { Background = new SolidColorBrush(Color.FromRgb(239, 243, 242)) };
        title.ColumnDefinitions.Add(new()); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(14, 0, 0, 0) };
        brand.Children.Add(Ui.Text("随引", 19, Ui.Green));
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
        var tabs = _sidebar = new TabControl { BorderThickness = new(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 224)), Background = Brushes.White };
        var follow = new StackPanel { Margin = new(16, 0, 16, 12) };
        follow.Children.Add(Ui.Heading("当前方向")); follow.Children.Add(_direction); follow.Children.Add(_directionKind);
        follow.Children.Add(Ui.Heading("同步字幕")); _caption.MinHeight = 50; follow.Children.Add(_caption);
        _subtitleSource.Margin = new(0, 6, 0, 4); follow.Children.Add(_subtitleSource);
        follow.Children.Add(Ui.Command("\uE8A5", "导入字幕", ImportSubtitles));
        follow.Children.Add(Ui.Command("\uE894", "清除导入字幕", () => { _cues = []; _lastCaption = ""; _subtitleSource.Text = "网页字幕"; }));
        _overlayToggle = Ui.Toggle("字幕与方向浮窗", _settings.SubtitleOverlay, value => { _settings.SubtitleOverlay = value; UpdateOverlay(); });
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
        _dragHandle = new Thumb { Style = (Style)FindResource("ImmersiveDragHandle"), ToolTip = "拖动小窗" };
        System.Windows.Automation.AutomationProperties.SetName(_dragHandle, "拖动小窗");
        _dragHandle.HorizontalAlignment = HorizontalAlignment.Left; _dragHandle.VerticalAlignment = VerticalAlignment.Top;
        _dragHandle.Margin = new(8); _dragHandle.Visibility = Visibility.Collapsed; _dragHandle.Opacity = 0.7;
        _dragHandle.DragDelta += (_, e) => QueueImmersiveDrag(e);
        _dragHandle.DragCompleted += (_, e) => FinishImmersiveDrag(e.Canceled);
        _workspace.Children.Add(_dragHandle);
        BuildSmallWindowControls();
        Grid.SetRow(_workspace, 2); _root.Children.Add(_workspace);

        var playback = new Grid { Margin = new(10, 4, 10, 4) };
        playback.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); playback.ColumnDefinitions.Add(new()); playback.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Ui.Icon("\uE892", "上一集", () => Fire(() => CommandAsync("previousEpisode"))));
        controls.Children.Add(Ui.Icon("\uE72B", "后退视频", () => Fire(() => CommandAsync("seek", -_settings.SeekSeconds))));
        _play = Ui.Icon("\uE768", "播放 / 暂停", () => Fire(() => CommandAsync("toggle"))); controls.Children.Add(_play);
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
                ResetDanmaku(); ResetOnlineVision(); UpdateDirection();
                _keys?.CancelTemporaryRate(); Fire(EndTemporaryRateAsync);
                if (_immersive) Fire(() => CommandAsync("focusOn"));
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "about"))
                { e.Cancel = true; _status.Text = "仅支持 http/https 网页和应用本地播放器。"; return; }
                UpdateHistory();
                ResetDanmaku(); ResetOnlineVision();
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
            if (!_smoke) StartChromeBridge();
            _saveTimer.Start(); UpdateOverlay();
            if (_smoke && _episodeProbe != null) Navigate(_episodeProbe);
            else if (_smoke) OpenDemo();
            else if (_initialMedia != null) NavigateMedia(_initialMedia);
            else Navigate("https://www.bilibili.com/");
            if (_store.LoadWarning != null) _status.Text = _store.LoadWarning;
            if (_smoke) _ = _danmakuProbe ? RunDanmakuProbeAsync() : _profileProbe != null ? RunProfileProbeAsync()
                : _episodeProbe != null ? RunBilibiliEpisodeProbeAsync()
                : _onlineVisionProbe ? RunOnlineVisionProbeAsync() : _smallWindowProbe ? RunSmallWindowProbeAsync() : RunSmokeTestAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "浏览器启动失败：" + ex.Message;
            if (_smoke) { WriteSmokeResult(false, ex.ToString(), []); Close(); }
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
                ResetDanmaku(); InvalidateOnlineSample();
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
                ResetDanmaku();
                _keys?.CancelTemporaryRate(); Fire(EndTemporaryRateAsync);
                _cues = []; _combatEvents.Clear(); _lastCaption = ""; _subtitleSource.Text = "网页字幕"; _notice = "";
                LoadVisualTrack();
            }
            if (_immersive && _mediaKey != mediaKey) Fire(() => CommandAsync("focusOn"));
            _mediaKey = mediaKey;
            _position = Finite(root, "time", 0); _duration = Finite(root, "duration", 0);
            _paused = root.GetProperty("paused").GetBoolean();
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

    private async Task CommandAsync(string action, double? value = null)
    {
        if (action is "position" or "seek" or "previousEpisode" or "nextEpisode")
        { InvalidateOnlineSample(); if (action is "position" or "seek") _videoSeeking = true; UpdateDirection(); }
        _notice = "";
        if (_browser.CoreWebView2 == null) { _status.Text = "浏览器尚未就绪。"; return; }
        var request = JsonSerializer.Serialize(new { action, value });
        var response = await _videoRouter!.ExecuteAsync("window.guideMate?.command(" + request + ") ?? false", action is "previousEpisode" or "nextEpisode");
        if (response == "false" || response == "null") _status.Text = _notice = action is "previousEpisode" or "nextEpisode" ? "本页未找到可用的上一集 / 下一集。" : MissingVideoNotice;
    }
    private async Task SetRateAsync(double rate)
    {
        _keys?.CancelTemporaryRate();
        await EndTemporaryRateAsync();
        _settings.Rate = Math.Clamp(rate, 0.25, 4);
        _temporaryRestoreRate = _settings.Rate;
        await CommandAsync("rate", _settings.Rate);
    }
    private async Task StartTemporaryRateAsync()
    {
        if (_temporaryRateActive || _duration <= 0) return;
        _temporaryRestoreRate = _settings.Rate; _temporaryRateActive = true;
        await CommandAsync("rate", _settings.TemporaryRate);
    }
    private async Task EndTemporaryRateAsync()
    {
        if (!_temporaryRateActive) return;
        _temporaryRateActive = false;
        await CommandAsync("rate", _temporaryRestoreRate ?? _settings.Rate);
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
        if (_overlay != null) { _settings.OverlayLeft = _overlay.Left; _settings.OverlayTop = _overlay.Top; _settings.OverlayWidth = _overlay.Width; }
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
            var restoreError = _keys.ResumeOrdinaryBindings();
            if (restoreError != null) _hotkeyStatus.Text = restoreError + "\n紧急恢复：" + _keys.EmergencyBinding;
        }
    }
    private void OnHotkey(string action)
    {
        switch (action)
        {
            case "PlayPause": Fire(() => CommandAsync("toggle")); break;
            case "SeekBack": Fire(() => CommandAsync("seek", -_settings.SeekSeconds)); break;
            case "SeekForward": Fire(() => CommandAsync("seek", _settings.SeekSeconds)); break;
            case "RateUp": Fire(() => SetRateAsync(_settings.Rate + 0.25)); break;
            case "RateDown": Fire(() => SetRateAsync(_settings.Rate - 0.25)); break;
            case "TemporaryRate": Fire(StartTemporaryRateAsync); break;
            case "PreviousEpisode": Fire(() => CommandAsync("previousEpisode")); break;
            case "NextEpisode": Fire(() => CommandAsync("nextEpisode")); break;
            case "Immersive": ToggleImmersive(); break;
            case "Hide": ToggleHidden(); break;
            case "ClickThrough": SetClickThrough(!_through); break;
            case "Emergency": EmergencyRestore(); break;
        }
    }
    private void SetClickThrough(bool enabled)
    {
        if (enabled && _keys?.EmergencyAvailable != true) { _throughToggle.IsChecked = false; _status.Text = "紧急恢复热键不可用，无法启用穿透。"; return; }
        if (_through == enabled) return;
        _through = enabled; NativeHotkeys.ClickThrough(this, enabled);
        if (_overlay?.IsVisible == true) NativeHotkeys.ClickThrough(_overlay, enabled);
        _throughToggle.IsChecked = enabled;
    }
    private void ToggleImmersive()
    {
        CancelImmersiveDrag();
        CancelEdgeSeek(); StopXRayTracking();
        // Refresh the WPF composition surface after changing a layered window's layout.
        _browser.Visibility = Visibility.Hidden;
        _immersive = !_immersive;
        _root.Background = _immersive ? Brushes.Transparent : Brushes.White;
        _edgeProgress.Visibility = _immersive ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowOpacity();
        Topmost = _settings.ShouldBeTopmost(_immersive);
        _workspace.Cursor = Cursors.Arrow;
        _workspace.ForceCursor = _immersive;
        foreach (var element in _chrome) element.Visibility = _immersive ? Visibility.Collapsed : Visibility.Visible;
        _sidebar.Visibility = _immersive ? Visibility.Collapsed : Visibility.Visible;
        _dragHandle.Visibility = _immersive ? Visibility.Visible : Visibility.Collapsed;
        if (_immersive)
        {
            _normalBounds = new(Left, Top, ActualWidth, ActualHeight);
            MinWidth = 320; MinHeight = 180;
            foreach (var row in new[] { 0, 1, 3, 4 }) _root.RowDefinitions[row].Height = new(0);
            _workspace.ColumnDefinitions[1].Width = new(0);
            Width = 640; Height = 360;
            Fire(() => CommandAsync("focusOn"));
        }
        else
        {
            MinWidth = 860; MinHeight = 500;
            _root.RowDefinitions[0].Height = new(46); _root.RowDefinitions[1].Height = new(44);
            _root.RowDefinitions[3].Height = new(54); _root.RowDefinitions[4].Height = new(28);
            _workspace.ColumnDefinitions[1].Width = new(284);
            Width = _normalBounds.Width; Height = _normalBounds.Height; Left = _normalBounds.Left; Top = _normalBounds.Top;
            Fire(() => CommandAsync("focusOff"));
        }
        UpdateClip(); UpdateDanmakuOverlay();
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
    private void UpdateOverlay()
    {
        if (!_settings.SubtitleOverlay || !IsVisible || WindowState == WindowState.Minimized) { _overlay?.Hide(); return; }
        if (_overlay == null)
        {
            _overlay = new() { Left = _settings.OverlayLeft, Top = _settings.OverlayTop, Width = Math.Clamp(_settings.OverlayWidth, 260, 1000) };
            _overlay.Show(); Ui.PlaceVisible(_overlay);
            _overlay.Closed += (_, _) => { _overlay = null; if (!_closing) { _settings.SubtitleOverlay = false; _overlayToggle.IsChecked = false; } };
        }
        else _overlay.Show();
        _overlay.Update(_lastCaption, CurrentDirection());
        NativeHotkeys.ClickThrough(_overlay, _through);
    }
    private void InitializeTray()
    {
        _tray = new Forms.NotifyIcon { Text = "随引", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("恢复窗口", null, (_, _) => Dispatcher.Invoke(EmergencyRestore));
        menu.Items.Add("播放 / 暂停", null, (_, _) => Dispatcher.Invoke(() => Fire(() => CommandAsync("toggle"))));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(EmergencyRestore);
    }

    private async Task RunSmokeTestAsync()
    {
        var checks = new List<string>();
        async Task WaitFor(Func<bool> condition, string description)
        {
            var end = DateTime.UtcNow.AddSeconds(25);
            while (!condition()) { if (DateTime.UtcNow > end) throw new Exception("Timeout: " + description); await Task.Delay(100); }
            checks.Add(description);
        }
        try
        {
            Left = -50000; Top = -50000; Ui.PlaceVisible(this); await Task.Delay(150);
            var titlePoint = PointToScreen(new Point(ActualWidth / 2, 20));
            var screenPoint = new System.Drawing.Point((int)titlePoint.X, (int)titlePoint.Y);
            if (!Forms.Screen.FromPoint(screenPoint).WorkingArea.Contains(screenPoint)) throw new Exception("Restored window title remains off-screen");
            checks.Add("off-screen window is restored using physical monitor coordinates");
            await WaitFor(() => _duration > 20 && _cues.Count > 0, "local HTML5 video and subtitle loaded");
            await CommandAsync("pause"); await WaitFor(() => _paused, "pause");
            await CommandAsync("position", 7); await WaitFor(() => Math.Abs(_position - 7) < 0.3, "seek to 7 seconds");
            if (_direction.Text != "东北") throw new Exception("Expected northeast direction, got " + _direction.Text);
            checks.Add("subtitle synchronizes to seek and northeast hint");
            await SetRateAsync(1.5); await WaitFor(() => _rate.SelectedItem is double rate && Math.Abs(rate - 1.5) < 0.01, "playback rate");
            await CommandAsync("play"); var start = _position;
            await WaitFor(() => !_paused && _position > start + 0.5, "real video clock advances");
            await CommandAsync("pause"); await WaitFor(() => _paused, "pause after playback");
            var beforeReload = _navigationVersion;
            _browser.CoreWebView2.Reload();
            await WaitFor(() => _navigationVersion > beforeReload && _duration > 35 && _position > 6 && _rate.SelectedItem is double restored && restored == 1.5, "reload restores time and rate");
            AddBookmark(); SaveSettings();
            if (_store.Load().Bookmarks.Count == 0) throw new Exception("Bookmark not saved"); checks.Add("bookmark persistence");
            _hole = true; UpdateClip();
            if (_root.Clip?.FillContains(new Point(_root.ActualWidth / 2, _root.ActualHeight / 2)) != false) throw new Exception("Hole not clipped");
            checks.Add("hole excludes center"); _hole = false; UpdateClip();
            if (_keys?.EmergencyAvailable == true)
            {
                SetClickThrough(true); if (!NativeHotkeys.IsClickThrough(this)) throw new Exception("Click-through flag missing");
                EmergencyRestore(); if (NativeHotkeys.IsClickThrough(this)) throw new Exception("Recovery did not clear click-through");
                checks.Add("native click-through and emergency restore");
            }
            await VerifyTopmostAsync(checks);
            await VerifyWindowCommandsAsync(checks);
            await VerifyImmersiveDragAsync(checks);
            await VerifySmallWindowAsync(checks);
            await VerifyCursorAsync(checks);
            await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').style.cursor = 'none'");
            ToggleImmersive(); await Task.Delay(500);
            if (ActualWidth > 700 || _root.RowDefinitions[0].Height.Value != 0) throw new Exception("Immersive layout failed");
            var focusedCursor = await _browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('video')).cursor");
            if (!_workspace.ForceCursor || _workspace.Cursor != Cursors.Arrow || focusedCursor != "\"default\"") throw new Exception("Immersive cursor was hidden: " + focusedCursor);
            checks.Add("immersive keeps arrow cursor despite player cursor:none");
            await VerifyImmersiveControlsAsync(checks);
            var layout = new { window = new { ActualWidth, ActualHeight }, browser = new { _browser.ActualWidth, _browser.ActualHeight, _browser.IsVisible }, workspace = new { _workspace.ActualWidth, _workspace.ActualHeight } };
            File.WriteAllText(Path.Combine(_dataPath, "immersive-layout.json"), JsonSerializer.Serialize(layout));
            await using (var preview = File.Create(Path.Combine(_dataPath, "immersive-preview.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
            if (_browser.ActualWidth < 300 || _browser.ActualHeight < 150) throw new Exception("Immersive browser has no layout area");
            ToggleImmersive(); checks.Add("immersive and normal layout");
            if (_workspace.ForceCursor) throw new Exception("Normal mode still forces cursor");
            var normalCursor = await _browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('video')).cursor");
            if (normalCursor != "\"none\"") throw new Exception("Focus off did not restore original cursor policy");
            await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').style.removeProperty('cursor')");
            checks.Add("normal mode restores page cursor CSS while native hidden-cursor protection remains active");
            _settings.SubtitleOverlay = true; UpdateOverlay(); checks.Add("subtitle overlay opened");
            _settings.PauseOnCombat = true; _settings.FadeOnCombat = true;
            await CommandAsync("position", 19.5);
            await WaitFor(() => Math.Abs(_position - 19.5) < 0.2, "seek before combat cue");
            await CommandAsync("play");
            await WaitFor(() => _position >= 20 && _paused && _faded, "subtitle combat pause and fade");
            _settings.PauseOnCombat = false; _settings.FadeOnCombat = false; EmergencyRestore();
            NavigateMedia(Path.Combine(AppContext.BaseDirectory, "assets", "demo.webm"));
            await WaitFor(() => _url.Contains("guidemate.local/local.html") && _duration > 35, "selected local media plays through restricted resource handler");
            if (_cues.Count != 0) throw new Exception("Old subtitles leaked to local media"); checks.Add("video change clears subtitles");
            await CommandAsync("pause"); await WaitFor(() => _paused, "local media pause");
            await CommandAsync("position", 12); await WaitFor(() => Math.Abs(_position - 12) < 0.2, "local media seek");
            AddBookmark();
            if (_store.Load().Bookmarks.All(v => v.LocalPath == null)) throw new Exception("Local bookmark path missing"); checks.Add("local bookmark persistence");
            var localBookmark = _store.Load().Bookmarks.First(v => v.LocalPath != null);
            OpenDemo(); await WaitFor(() => _url.Contains("/demo.html") && _cues.Count > 0, "navigate away from local media");
            NavigateSaved(localBookmark);
            await WaitFor(() => _url.Contains("/local.html") && Math.Abs(_position - 12) < 0.2 && _localFile == localBookmark.LocalPath, "local bookmark reopens file and position");
            await _browser.CoreWebView2.ExecuteScriptAsync("""
                window.guideMateMediaProbe = null;
                (async () => {
                  const url = 'https://media.guidemate.local/video';
                  const partial = await fetch(url, {headers: {'Range': 'bytes=0-15'}});
                  const data = new Uint8Array(await partial.arrayBuffer());
                  const head = await fetch(url, {method: 'HEAD'});
                  const invalid = await fetch(url, {headers: {'Range': 'bytes=999999999-'}});
                  window.guideMateMediaProbe = {partial: partial.status, length: data.length, magic: [...data.slice(0, 4)], head: head.status, invalid: invalid.status};
                })().catch(error => { window.guideMateMediaProbe = {error: error.message}; });
                """);
            var probe = "null";
            for (var attempt = 0; attempt < 50 && probe == "null"; attempt++)
            { await Task.Delay(100); probe = await _browser.CoreWebView2.ExecuteScriptAsync("window.guideMateMediaProbe"); }
            using (var parsed = JsonDocument.Parse(probe))
            {
                var result = parsed.RootElement;
                if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("error", out _)
                    || result.GetProperty("partial").GetInt32() != 206 || result.GetProperty("length").GetInt32() != 16
                    || result.GetProperty("head").GetInt32() != 200 || result.GetProperty("invalid").GetInt32() != 416
                    || result.GetProperty("magic")[0].GetInt32() != 0x1A) throw new Exception("Local media Range/HEAD probe failed: " + probe);
            }
            checks.Add("local media HTTP 206 byte range, HEAD and 416 invalid range");
            await SetRateAsync(1.5); _settings.TemporaryRate = 3;
            await StartTemporaryRateAsync();
            await WaitFor(() => _rate.SelectedItem is double boosted && boosted == 3, "temporary speed applies to real video");
            if (_settings.Rate != 1.5 || CurrentVideo().Rate != 1.5) throw new Exception("Temporary speed overwrote saved base rate");
            checks.Add("temporary speed preserves saved base rate");
            await EndTemporaryRateAsync();
            await WaitFor(() => _rate.SelectedItem is double normal && normal == 1.5, "temporary speed restores after release");
            await StartTemporaryRateAsync(); EmergencyRestore();
            await WaitFor(() => !_temporaryRateActive && _rate.SelectedItem is double normal && normal == 1.5, "emergency restores temporary speed");
            Navigate("https://guidemate.local/iframe-test.html");
            await WaitFor(() => _url.Contains("iframe-test") && _duration > 35 && _videoRouter!.ActiveFrame != 0, "cross-origin iframe video discovered");
            await CommandAsync("pause"); await CommandAsync("position", 9);
            await WaitFor(() => _paused && Math.Abs(_position - 9) < 0.2, "cross-origin iframe pause and seek routed");
            ToggleImmersive(); await Task.Delay(700);
            var iframeFocus = await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#player').hasAttribute('data-guidemate-target') && document.querySelector('#player').getBoundingClientRect().width >= innerWidth-2");
            if (iframeFocus != "true") throw new Exception("Iframe did not expand for immersive mode: " + iframeFocus);
            checks.Add("cross-origin immersive expands parent iframe");
            await Task.Delay(2100);
            if (await _videoRouter!.ExecuteAsync("!document.querySelector('video').controls") != "true") throw new Exception("Iframe native controls did not auto-hide");
            checks.Add("cross-origin immersive also auto-hides native controls");
            ToggleImmersive(); await Task.Delay(400);
            if (await _videoRouter.ExecuteAsync("document.querySelector('video').controls") != "true") throw new Exception("Iframe controls not restored after immersive");
            checks.Add("cross-origin controls restore after immersive exit");
            var frameVisibility = await _videoRouter!.ExecuteAsync("document.querySelector('video').getBoundingClientRect().width");
            if (!double.TryParse(frameVisibility, CultureInfo.InvariantCulture, out var frameWidth) || frameWidth < 300) throw new Exception("Iframe layout missing");
            Navigate("https://guidemate.local/iframe-test.html?nested=1");
            await WaitFor(() => _url.Contains("nested") && _duration > 35 && _videoRouter.ActiveFrame != 0, "nested cross-origin iframe discovered");
            await CommandAsync("position", 13);
            await WaitFor(() => Math.Abs(_position - 13) < 0.2, "nested iframe controls routed");
            await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#player').remove()");
            await WaitFor(() => _duration == 0, "removed iframe stops stale state and hidden iframe is ignored");
            Navigate("https://guidemate.local/episodes-test.html");
            await WaitFor(() => _url.Contains("episodes-test") && _duration > 35, "episode fixture loaded");
            LoadSubtitles(Path.Combine(AppContext.BaseDirectory, "assets", "demo.srt"));
            await CommandAsync("position", 10); await CommandAsync("nextEpisode");
            await WaitFor(() => _url.Contains("episode=2") && _position < 1 && _cues.Count == 0, "next episode resets time and imported subtitles");
            await CommandAsync("previousEpisode");
            await WaitFor(() => _url.Contains("episode=1"), "previous episode works");
            await CommandAsync("previousEpisode");
            if (!_notice.Contains("上一集")) throw new Exception("Missing episode boundary feedback");
            checks.Add("episode boundaries report unavailable command");
            await VerifyBilibiliEpisodesAsync(checks);
            var cookieName = "GuideMate_Synthetic_" + Guid.NewGuid().ToString("N");
            var cookieResult = await ImportCookiesAsync([new() { Name = cookieName, Value = "test-only", Domain = "www.bilibili.com", Path = "/", Session = true, Secure = true, HttpOnly = true, SameSite = "strict" }]);
            var importedCookie = (await _browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.bilibili.com/")).SingleOrDefault(c => c.Name == cookieName);
            if (!cookieResult.Success || importedCookie is not { IsHttpOnly: true, IsSecure: true, IsSession: true, SameSite: CoreWebView2CookieSameSiteKind.Strict }) throw new Exception("Synthetic cookie attributes were not imported");
            _browser.CoreWebView2.CookieManager.DeleteCookie(importedCookie);
            checks.Add("synthetic cookie import preserves secure httponly session and samesite");
            var expires = DateTimeOffset.UtcNow.AddHours(1);
            var persistentResult = await ImportCookiesAsync([new() { Name = cookieName, Value = "test-only", Domain = "www.bilibili.com", Path = "/guidemate-test", Session = false,
                Secure = true, HttpOnly = false, SameSite = "no_restriction", ExpirationDate = expires.ToUnixTimeSeconds() }]);
            var persistentCookie = (await _browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.bilibili.com/guidemate-test")).SingleOrDefault(c => c.Name == cookieName);
            if (!persistentResult.Success || persistentCookie is not { IsSession: false, IsSecure: true, SameSite: CoreWebView2CookieSameSiteKind.None }
                || Math.Abs((persistentCookie.Expires - expires.UtcDateTime).TotalSeconds) > 2) throw new Exception("Persistent cookie expiry or SameSite None changed");
            _browser.CoreWebView2.CookieManager.DeleteCookie(persistentCookie);
            checks.Add("synthetic persistent cookie preserves expiry path and samesite none");
            await VerifyChromeHostAsync(checks);
            if (_keys != null)
            {
                if (NativeHotkeys.Format(Key.K, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift) != "Ctrl+Alt+Shift+K"
                    || NativeHotkeys.Format(Key.D3, ModifierKeys.Control | ModifierKeys.Shift) != "Ctrl+Shift+3"
                    || NativeHotkeys.Parse("Ctrl+Shift+3").Key != KeyInterop.VirtualKeyFromKey(Key.D3)) throw new Exception("Recorded key format is incorrect");
                checks.Add("recorded modifiers and numeric keys roundtrip");
                _keys.SuspendOrdinaryBindings();
                if (!_keys.OrdinaryBindingsSuspended) throw new Exception("Ordinary bindings were not suspended");
                var restoreError = _keys.ResumeOrdinaryBindings();
                if (restoreError != null || _keys.OrdinaryBindingsSuspended) throw new Exception("Ordinary bindings were not restored: " + restoreError);
                checks.Add("ordinary hotkeys suspend and restore for recording");
                var duplicate = new Dictionary<string, string>(_settings.Hotkeys) { ["SeekBack"] = _settings.Hotkeys["PlayPause"] };
                if (_keys.Apply(duplicate) == null) throw new Exception("Duplicate hotkey was accepted");
                checks.Add("duplicate hotkey rejected without replacing active bindings");
            }
            if (_initialMedia != null) await VerifyVisionAsync(checks);
            await VerifyMouseHotkeysAsync(checks);
            await using (var stream = File.Create(Path.Combine(_dataPath, "browser-preview.png")))
                await _browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            WriteSmokeResult(true, "", checks);
        }
        catch (Exception ex) { WriteSmokeResult(false, ex.ToString(), checks); }
        finally { Close(); }
    }
    private void WriteSmokeResult(bool success, string error, List<string> checks)
    {
        Directory.CreateDirectory(_dataPath);
        File.WriteAllText(Path.Combine(_dataPath, "smoke-result.json"), JsonSerializer.Serialize(new { success, error, checks, runtime = Environment.Version.ToString(), time = DateTime.Now }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
