# 安全与隐私

随引使用本机 WebView2 资料目录保存网站会话，热键、收藏、进度与校准保存于本地设置；视觉分析在本机执行。Chrome 接入范围限定为 B 站，并在用户配对及确认后导入会话。不要将这些资料上传到 Issue、Pull Request 或公开仓库。

## 报告问题

影响账号会话、本地文件访问、Chrome Native Messaging 来源验证、配对流程或网页桥边界的问题，请使用 GitHub 仓库的 **Security → Report a vulnerability** 私密报告。维护者应先在仓库设置启用 Private vulnerability reporting；入口不可用时，请先通过不含漏洞细节的 Issue 请求私密联系方式。

报告中提供版本、Windows/WebView2 版本、影响范围和使用合成数据的复现方法。不要附带真实 Cookie、登录二维码、配对码、完整浏览器资料或用户配置。

目前以最新发布版本为维护目标。第三方网站改版、后台播放策略及热键冲突可按普通兼容性问题报告；公开报告同样需要去除个人信息。
