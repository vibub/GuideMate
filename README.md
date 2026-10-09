# 随引（GuideMate）

<p align="center">
  <img src="assets/branding/guidemate.png" alt="随引图标" width="128" />
</p>

Windows 攻略视频跟随工具。把视频、字幕和方向提示放在顺手的位置，让你专注游戏。

- **跟随播放**：置顶小窗、全局热键、播放进度记忆与收藏。
- **沉浸观看**：鼠标穿透、X 光透明孔、底边进度线和 B 站弹幕。
- **攻略提示**：同步字幕，或识别原神、终末地攻略视频中的小地图箭头。
- **资料沿用**：更新程序时继续使用原有设置、窗口偏好和网页登录资料。

## 快速开始

适用于 Windows 10 1809 及以上版本和 Windows 11（x64）。运行需要 [.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

从 [GitHub Releases](https://github.com/vibub/GuideMate/releases) 下载 `GuideMate-版本-win-x64.zip`，完整解压后运行 `Start-GuideMate.cmd` 或 `GuideMate.exe`。不要单独取出 EXE。发布包不含 .NET 运行时或 SDK；缺少所需运行时会显示安装提示。首次启动默认打开 B 站。

## 常用热键

| 热键 | 操作 |
| --- | --- |
| `Ctrl+Alt+Space` | 播放 / 暂停 |
| `Ctrl+Alt+Left / Right` | 后退 / 快进 |
| `Ctrl+Alt+Up / Down` | 调整倍速 |
| `Ctrl+Alt+Shift+V` | 按住临时倍速 |
| `Ctrl+Alt+PageUp / PageDown` | 上一集 / 下一集 |
| `Ctrl+Alt+Shift+I` | 沉浸模式 |
| `Ctrl+Alt+D` | B 站全屏弹幕 |
| `Ctrl+Alt+H` | 隐藏 / 恢复 |
| `Ctrl+Alt+P` | 鼠标穿透 |
| `Ctrl+Alt+F10` | 紧急恢复：退出沉浸并解除穿透 |

热键可在设置中录制，也支持 Mouse4 / Mouse5。可配置项目、备用紧急恢复键和长按行为见[使用指南](docs/USER_GUIDE.md)。

## 文档

| 文档 | 内容 |
| --- | --- |
| [使用指南](docs/USER_GUIDE.md) | 播放、字幕、沉浸小窗、弹幕、视觉提示与资料目录 |
| [Chrome B 站登录同步](docs/CHROME_SYNC.md) | 限定站点、授权步骤与数据边界 |
| [原神视觉说明](docs/GENSHIN.md) · [终末地适配说明](docs/ENDFIELD.md) | 校准、识别范围与验收边界 |
| [实现文档](docs/IMPLEMENTATION.md) | 架构、技术选型与功能范围 |
| [构建与验证](docs/TESTING.md) · [验收记录](docs/VERIFICATION.md) | 本地检查、隔离桌面验证与已知限制 |
| [贡献指南](CONTRIBUTING.md) · [安全说明](SECURITY.md) | 开发约定、问题反馈与安全报告 |

## 从源码运行

在 Windows 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，克隆仓库后运行根目录的 `Start-GuideMate.cmd`。常用构建和回归命令：

    dotnet build src/GuideMate.App/GuideMate.App.csproj -c Release
    ./scripts/Test-GuideMate.ps1

更完整的桌面验证和发布步骤见[构建与验证](docs/TESTING.md)。

## 资料与范围

默认资料目录是 `%LOCALAPPDATA%\GuideMate`。显式选择 `--data-dir` 后，随引会记住该目录；后续启动和程序更新继续沿用它。设置保存在 `settings.json`，网页登录资料位于 `WebView2` 子目录。发布只更新程序和资源，不覆盖这些资料。

Chrome 会话接入仅限 B 站，并由用户授权注册、配对和导入。视觉提示用于攻略视频方向辅助，不等同于实机导航、绝对坐标或路线匹配；不同网站的播放器结构和权限也会影响兼容性。

## 许可证

项目原创代码使用 [MIT 许可证](LICENSE)。依赖及素材声明见 [第三方声明](THIRD_PARTY_NOTICES.md)。
