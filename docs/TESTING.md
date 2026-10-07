# 构建与验证

## 命令行回归

在 Windows x64、.NET 10 SDK、Node.js 24 环境执行；视觉检查还需要 PATH 中已有的 FFmpeg 与相邻 ffprobe。

```powershell
dotnet build src/GuideMate.App/GuideMate.App.csproj -c Release
dotnet build src/GuideMate.ChromeHost/GuideMate.ChromeHost.csproj -c Release
dotnet run --project tests/GuideMate.Specs/GuideMate.Specs.csproj -c Release
node tests/chrome.spec.cjs
node tests/bridge-subtitles.spec.cjs
dotnet run --project tests/GuideMate.Vision.Specs/GuideMate.Vision.Specs.csproj -c Release
```

Specs 是遇到失败即退出的控制台检查。核心检查涵盖字幕、方向规则、持久化、资料目录、校准和热键手势；JavaScript 检查覆盖合成 Chrome Cookie 协议与 B 站字幕 DOM；默认视觉检查覆盖合成旋转、非箭头拒识及 FFmpeg 查找，不需要私有录像。

CI 在 Windows runner 执行这些命令并打包，产物作为 workflow artifact 保存。CI 不运行真实账号导入、桌面 WebView2 冒烟或游戏前台测试。

## 隔离桌面冒烟

需要交互桌面及 WebView2 Runtime。在新的临时目录测试，参数必须同时包含 `--smoke-test`，不要使用正式资料目录：

```powershell
$testProfile = Join-Path $env:TEMP ('GuideMate-smoke-' + [guid]::NewGuid().ToString('N'))
& ./artifacts/GuideMate-win-x64/GuideMate.exe --smoke-test --data-dir $testProfile
```

程序完成后退出，结果写入测试目录。这个测试使用真实 WPF/WebView2，部分输入为应用内模拟事件；它不能证明真实鼠标驱动、物理按键、管理员窗口、游戏前台或所有网站兼容。

## 发布前人工检查

检查普通/最大化/沉浸、最小化/托盘恢复、鼠标指针、小窗拖动缩放、透明度/X 光、底边跳转、字幕换行与选区跨导航保留。对 B 站切集，确认网址 p 参数及实际视频变化；不将“命令已执行”当作成功。使用实际侧键分别验证短按、长按、松开及前台切换，并记录环境。

真实 Chrome 接入需要用户明确授权。合成 Cookie 的成功不代表真实账号导入或登录成功。在线取帧的成功也不等于整段攻略的方向准确率。

本机历史验收记录和 artifacts 由开发者本地保留，不随公开源码或发布包分发。公开验证应给出命令、环境、结果和适用范围，避免上传账号资料。
