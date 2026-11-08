# 地图资源来源

随引的“内置提瓦特地表地图”表示程序预置资源入口和格式支持，首次启用时自动获取资源；发布 ZIP 不直接包含第三方地图文件。

- 上游：[BetterGI.Assets.Map 1.0.25](https://www.nuget.org/packages/BetterGI.Assets.Map/1.0.25)，作者 huiyadanli。
- 资源仓库：[babalae/bettergi-libraries](https://github.com/babalae/bettergi-libraries)。
- 固定下载地址：`https://api.nuget.org/v3-flatcontainer/bettergi.assets.map/1.0.25/bettergi.assets.map.1.0.25.nupkg`。
- 包大小：215,576,403 字节；SHA-256：`278EE3542E8C255BF35C70E7CD896F7AB991E1CEEACDD3A4D6326FFD0CE5AAD0`。
- 仅安装 `Teyvat_0_2048_SIFT.kp.bin` 和 `Teyvat_0_2048_SIFT.mat.png`，二者分别验证固定 SHA-256；临时下载和提取文件使用后删除。
- 不安装上游程序或执行包内代码。解析器按二进制关键点和灰度 PNG 描述子格式独立实现，沿用地表地图像素坐标。

核查时，资源包 nuspec 未声明许可证，资源仓库根目录未提供 LICENSE；不能将 BetterGI 主程序的 GPL-3.0 直接视为所有地图资源的再分发授权。地图资源版权归相应权利人，随引的 MIT 许可证不授予这些资源的版权。当前选择由用户启用后从上游公开地址获取并本地缓存，不在随引仓库或发布包中再分发。若后续改为随包分发，应先确认对应资源的明确授权。

固定版本及摘要避免上游变动造成无提示替换；新地图资源需要单独核验和更新程序中的版本、尺寸及摘要。
