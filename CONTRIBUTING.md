# 参与开发

欢迎通过 Issue 描述问题或提出改进，再通过 Pull Request 提交修改。请给出可复现步骤、预期行为与实际结果；截图和日志需要去除账号、Cookie、配对码及个人路径。

## 开发环境

- Windows 10 1809 或更新版本、Windows 11，x64。
- .NET 10 SDK；`global.json` 允许同一 10.0 系列的更新 feature band。
- Microsoft Edge WebView2 Evergreen Runtime。
- Node.js 24，用于无第三方依赖的 JavaScript 回归。
- 视觉回归还需要 PATH 中的 FFmpeg 与相邻的 ffprobe；Python/Pillow 仅用于重新生成在线视觉夹具。

构建和检查命令见 [测试说明](docs/TESTING.md)。两个 .NET Specs 项目是控制台检查程序，使用 `dotnet run`，不是 `dotnet test`。

## 必须保留的行为

- 中文产品名为“随引”，工程、程序与资料入口继续使用 GuideMate。
- 更新只替换程序和资源，保留用户的热键、收藏、进度、校准、窗口设置及 WebView2 账号资料。
- 正常启动沿用已记录的数据目录。自动化桌面测试使用独立目录及 `--smoke-test`，不改变正式资料选择，不自动启动正式软件。
- 发布前让运行中的程序正常退出，不强杀；不用默认配置覆盖已有设置。
- 未绑定的侧键透传；普通操作不能共用热键，长按临时倍速可与一个普通操作共用。取消或切换前台不补发短按操作。
- 不改 Windows 全局分屏或鼠标设置。小窗透明度、X 光、拖动、缩放、进度和紧急恢复须继续协作。
- Chrome 会话接入仅限 B 站。真实注册、配对和导入须用户明确授权；测试仅使用合成 Cookie。
- 区分合成测试、真实桌面窗口、真实站点和物理按键/游戏前台验证，不能扩大验收结论。

不要提交 `artifacts`、`settings.json`、`active-profile.json`、WebView2 浏览器资料或未获授权的攻略视频。截图应遮挡 UID、账号和聊天内容。代码与原生依赖发生变化时，更新对应测试与第三方声明。

提交 Pull Request 时说明触发条件、修改后的行为和实际完成的验证。贡献的原创内容采用本仓库的 [MIT 许可证](LICENSE)。
