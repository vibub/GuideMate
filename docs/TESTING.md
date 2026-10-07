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

## 全屏弹幕检查

命令行回归包含 `node tests/bridge-danmaku.spec.cjs`，覆盖当前播放器模型字段、重复快照身份、屏蔽与隐藏条目、普通/顶部/底部/反向模式、弹幕开关、沉浸隐藏原网页后提取、跳转代次、消息上限和其他站点隔离。这些为离线模型检查。

构建后的程序可用以下命令运行独立 WPF/WebView2 弹幕检查；将 PROFILE 替换为新的测试目录，不使用正式资料目录。测试结束自动退出，产生 smoke-result.json、danmaku-overlay.png 和 danmaku-player.png。

```powershell
& ./src/GuideMate.App/bin/Release/net10.0-windows10.0.17763.0/GuideMate.exe --smoke-test --danmaku-probe --data-dir PROFILE
& ./src/GuideMate.App/bin/Release/net10.0-windows10.0.17763.0/GuideMate.exe --smoke-test --bilibili-danmaku-live 'https://www.bilibili.com/video/BV14Z421L7DN/' --data-dir PROFILE
```

第一条使用独立测试虚拟域名 danmaku-test.bilibili.com 和合成渲染实例，验证完整显示器物理边界、透明窗口样式、非激活、去重、暂停与倍速、开关保存、隐藏/最小化恢复及导航清理。第二条加载真实 B 站页面并等待实际播放器与弹幕数据；无播放器或无弹幕时失败，不能用夹具成功替代真实站点验收。两者均不能替代物理鼠标输入、原神前台或独占全屏游戏验收。

2026-10-08 本机验收：弹幕桥接 11 项、既有字幕桥接 30 项、Core 82 项和 Chrome 合成协议检查通过；隔离 WPF 弹幕检查 28 项通过；使用未登录独立 WebView2 资料加载真实 BV14Z421L7DN，25 项在线弹幕检查通过，已核对覆盖层与播放器截图。真实站点检查包括实际弹幕进入覆盖层、完整显示器原生边界、不抢焦点、穿透样式、暂停/倍速、隐藏/最小化恢复及导航清理。尚未进行物理鼠标、原神前台和独占全屏游戏实测。
既有完整隔离桌面回归 233 项、视觉 47 项及 Chrome 宿主构建通过；本地发布版弹幕 28 项检查通过。发布前后正式资料目录选择、settings.json 哈希及其 453 个文件的元数据摘要完全一致，未自动打开正式软件。

## 沉浸热键提示与悬停拖动按钮

`--smoke-test --small-window-probe --data-dir PROFILE` 在独立资料目录同时检查原小窗效果与新热键提示；完整桌面 smoke 也包含这些检查。测试使用真实 WebView2 示例视频执行快进/后退、播放/暂停、普通与临时倍速，验证自定义跳转时长、连续操作的最后一次结果、恢复原倍速、不成功操作的失败提示、1.4 秒收起与重复提示重新计时；验证提示避让拖动按钮且不拦截输入。悬停使用合成局部坐标，拖动捕获使用 WPF 鼠标事件启动真实 Thumb 捕获状态；不能替代物理鼠标及游戏前台测试。隐藏、最小化和退出沉浸会清空提示并停止悬停轮询，恢复时重新启用。

2026-10-08 热键提示验收：完整隔离桌面回归 268 项、Core 82 项、字幕桥接 30 项及弹幕/播放状态桥接 12 项通过；发布版小窗与热键提示 79 项检查通过并核对截图。独立小窗测试先等待初次页面导航完成，再采样用于隐藏恢复比较的页面状态；恢复检查保留具体失败状态诊断。物理鼠标与原神前台尚未实测。

最终发布版复核：小窗与提示 79 项、全屏弹幕 28 项通过。正式目录选择及配置 SHA256、453 个资料文件的元数据摘要与发布前一致；测试均在独立目录结束后退出，未自动启动正式软件。
