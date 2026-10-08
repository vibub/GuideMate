# 第三方组件与素材

随引的原创代码采用 [MIT](LICENSE)。以下组件保留各自许可证；项目许可证不替代其许可条款。版本以项目文件和实际发布包为准。

| 组件 | 当前直接依赖 | 许可证/说明 | 来源 |
| --- | --- | --- | --- |
| WPF UI / WPF UI Abstractions | 4.3.0 | MIT，全文见 licenses/WPF-UI-LICENSE.txt | https://github.com/lepoco/wpfui |
| Microsoft.Web.WebView2 SDK | 1.0.4258.31 | 包内 BSD 条款，全文见 licenses/WEBVIEW2-LICENSE.txt | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31 |
| OpenCvSharp4 / Windows runtime | 4.13.0.20260627 | Apache-2.0，Copyright 2008-2026 shimat；全文见 licenses/OPENCVSHARP-LICENSE.txt | https://github.com/shimat/opencvsharp |
| OpenCV | 原生 runtime 内组件 | Apache-2.0，全文见 licenses/OPENCV-LICENSE.txt | https://github.com/opencv/opencv |
| OpenCV FFmpeg videoio 插件 | opencv_videoio_ffmpeg4130_64.dll | LGPL-2.1-or-later；全文见 licenses/OPENCV-FFMPEG-LICENSE.txt | https://github.com/opencv/opencv_3rdparty/tree/ffmpeg/4.x |
| .NET / WPF / Windows Forms | 自包含发布时随包提供 | Microsoft .NET Library 许可及组件第三方声明；见 licenses/DOTNET-LICENSE.txt、licenses/DOTNET-THIRD-PARTY-NOTICES.txt | https://github.com/dotnet/runtime |

Microsoft Edge WebView2 **Runtime** 由用户安装，适用微软的独立分发与使用条款；本包不打包整个浏览器。NuGet 传递依赖和原生库所含第三方组件亦适用其原始条款，更新依赖时须检查对应声明。

本地离线视觉分析另调用用户已安装的 FFmpeg/ffprobe，发布脚本不复制其可执行文件。OpenCV runtime 自带的 videoio 插件是另一个独立组件：它保持独立 DLL 分发，源码与构建说明入口见上表；发布前核对实际二进制对应的源码和 LGPL 要求。

`assets/demo.webm`、`assets/online-vision-*.webm` 是生成的离线验证素材；字幕和页面是配套夹具。用户提供的攻略录像、截图、识别缓存与浏览器资料不在仓库中。B 站、YouTube、原神及其他名称仅用于说明兼容对象，相关商标和用户提供的素材归原权利人所有。
