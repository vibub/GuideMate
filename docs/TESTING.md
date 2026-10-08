# 构建与验证

## 核心回归

在 Windows x64、.NET 10 SDK、Node.js 24 环境执行：

```powershell
./scripts/Test-GuideMate.ps1
```

保留两个 .NET 控制台 Specs 项目和三组零依赖 JavaScript 检查，覆盖字幕、方向规则、设置与资料目录、校准、热键手势、Chrome 合成协议、网页字幕和弹幕提取。视觉检查还需要 PATH 中已有的 FFmpeg 与相邻 ffprobe。

历史上编进正式应用的桌面冒烟方法、在线探针及专用夹具已移除。正式程序不再携带测试代码；侧栏的离线演示仍保留。CI 执行核心回归并在全新目录打包，不读取本机账号或私有录像。

## 隔离桌面检查

需要交互桌面与 WebView2 Runtime。`--isolated` 使用独立配置，不读取或更新正式资料目录选择，也不启动 Chrome 配对服务。未指定目录时创建新的临时目录。它用于人工检查，不自动执行冒烟或退出：

```powershell
$testProfile = Join-Path $env:TEMP ('GuideMate-check-' + [guid]::NewGuid().ToString('N'))
& ./artifacts/GuideMate-win-x64/GuideMate.exe --isolated --data-dir $testProfile
```

检查完成后正常关闭测试窗口。检查普通/最大化/沉浸、最小化与托盘恢复、鼠标指针、小窗拖动缩放、透明度与 X 光、底边跳转、字幕换行和校准跨导航保留。不要将测试目录设为正式资料目录。

全屏弹幕使用独立进程的原生窗口和 DirectComposition 图层；新文字上传一次，DWM 负责滚动与到期隐藏，不再使用 WPF 整屏逐帧重绘。沉浸拖动使用 16 ms 输入优先级计时合并目标位置，不等待 `CompositionTarget.Rendering`；松开立即应用最终目标，取消时停止计时并丢弃目标。拖动同一显示器上的小窗不重复传输弹幕快照，也不重新校准弹幕时钟。检查弹幕开关、字号、速度、区域和透明度，以及暂停、倍速、跳转、跨显示器、隐藏、最小化和退出沉浸。原生窗口的鼠标穿透、无背景透明显现及实际拖动延迟必须在允许打开桌面窗口后验收。

对 B 站切集，确认网址 p 参数与实际视频变化。`bridge-danmaku.spec.cjs` 使用离线模型，不代表真实站点兼容。拖动延迟测量使用真实桌面窗口和合成弹幕，不代表物理鼠标或游戏前台验收。

真实 Chrome 注册、配对和导入需要用户明确授权。合成 Cookie 不代表真实账号同步。截图与日志不得包含账号、Cookie 或配对码。

## 发布包

```powershell
./scripts/Package-GuideMate.ps1 -Version v0.0.0-local
```

打包使用新的暂存目录，生成 ZIP 与 SHA256 后清理本次暂存目录。WPF 和 Chrome 桥接均依赖系统 .NET 10，采用普通多文件发布，应用及第三方 DLL 与 EXE 分开存放，不包含 .NET 运行库，也不附带 SDK XML 文档。保留中英文资源；框架依赖发布不做程序集裁剪，Chrome 桥接继续使用类型明确的 JSON 元数据。托管与原生库从发布目录加载，不再进行单文件合并、压缩或启动解包。OpenCV 使用同版本官方 Windows Slim 包，保留实际使用的图像处理模块；离线分析继续调用用户已有的 FFmpeg/ffprobe。

更新只替换程序文件，保留 settings.json、WebView2、视觉缓存和正式资料目录。升级旧的多文件版本时，将发布包完整解压到新的程序目录，避免覆盖后残留旧 DLL；程序继续沿用正式资料目录。源码发布也应指定全新输出目录。测量包体积以全新暂存目录生成的 ZIP 为准，不得通过清空整个旧目录来更新软件。

无需打开窗口的验证包括发布包文件清单、独立 runtimeconfig 的共享框架声明、DLL 依赖完整性与 Windows GUI apphost 检查。确认包内没有 coreclr、hostfxr、System.Private.CoreLib 或 WPF/.NET 系统运行库。桌面允许测试时，再在隔离环境验证没有 .NET、只有普通 .NET Runtime、装好 Desktop Runtime 三种启动状态，及全屏弹幕下的真实拖动。

多文件发布后，用隔离目录检查 WebView2 本地视频加载、OpenCV 校准预览、Chrome 合成协议及全屏弹幕子进程启动/退出。资源路径使用 `AppContext.BaseDirectory`，同程序子进程使用 `Environment.ProcessPath`，完整复制发布目录，不单独复制 EXE。

旧 `--smoke-test` 参数继续作为隔离模式兼容入口，避免旧测试命令访问正式资料；它不再自动执行检查或退出。
