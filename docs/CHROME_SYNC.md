# Chrome B 站登录同步

范围固定为 `bilibili.com` 及其子域。组件已实现、已使用合成 Cookie 跑通；尚未读取用户真实 Cookie，也未确认真实账号登录成功。不是 Chrome 整个账号、书签、历史或密码同步。

## 首次接入

1. 先打开随引发布版。
2. 明确同意本机通信权限后，运行 `powershell -NoProfile -File scripts/Register-ChromeBridge.ps1`。该操作只在当前用户 `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.guidemate.bilibili` 注册组件，不需要管理员权限。
3. 在 Chrome 扩展管理页由用户自行加载已解压的扩展目录 `artifacts/GuideMate-win-x64/chrome-extension`。扩展固定 ID 为 `emeedledfchopaemhkhjfpbppffbjhid`。
4. 在要同步的 Chrome 配置中自行登录 B 站。打开随引的“Chrome 登录同步”，复制本次配对码，在扩展弹窗中粘贴并点击“同步到随引”。
5. 随引显示 Cookie 数量和站点范围，由用户确认导入。成功后自动刷新当前 B 站页面。B 站仍可要求重新登录或验证，不绕过认证。

注册需用户明确授权后手动执行。Chrome 扩展加载及真实账号导入须由用户操作并验证；合成链路检查不等同于已经完成真实接入。

## 数据路径与限制

扩展只请求 `cookies`、`nativeMessaging` 和 B 站主机权限。它不扫描 Chrome 文件、不解密数据库、不读取密码库、不获取其他站点数据，不申请后台常驻定时同步，也没有上传端点。

点击同步后，扩展通过 Chrome 的 Native Messaging 启动 `chrome-host/GuideMate.ChromeHost.exe`。本机组件验证固定扩展来源，把有界的 JSON 请求转发到当前用户命名管道。还会校验接收进程就是该发布目录的 `GuideMate.exe`，拒绝其他程序占用同名管道。随引验证本次 128 位配对码，随后弹出导入确认。重新启动会生成新配对码。

协议使用 4 字节小端长度前缀，单条消息最多 1 MiB。限制 Cookie 数量与字段长度，拒绝相似域名、无效属性和过期格式。导入保留 Domain、Path、Secure、HttpOnly、SameSite 和会话/有效期属性。分区 Cookie 暂不支持，扩展会跳过并报告数量。

Cookie 只在本机内存中经过扩展和本机组件，最终由 WebView2 存入自己的用户数据目录；不写入 `settings.json`、调试日志或额外 Cookie 导出文件。配对码不保存到配置。程序仅更新请求中提供的 Cookie，不删除 Chrome 数据，也不清空其他 WebView2 站点数据。真实登录有效性由 B 站判断，不能只凭写入成功声称登录成功。

Native Messaging 组件必须与其随引发布版位于同一发布目录。移动发布目录后需重新注册路径。不同 Chrome 配置的会话不同，需要在目标配置中安装并点击扩展。它不是实时双向同步，也不复制 localStorage、IndexedDB 或扩展数据。

## 撤销

运行 `powershell -NoProfile -File scripts/Register-ChromeBridge.ps1 -Unregister` 删除随引专用注册项，再由用户从 Chrome 移除该扩展。撤销注册不删除浏览器数据或随引已有会话；可在随引内的 B 站页面自行退出账号。

## 验证

- `node tests/chrome.spec.cjs` 检查域名过滤、过期与分区处理、调用来源、配对格式以及未登录情况。
- 核心测试检查消息长度、字段、相似域名拒绝和设置迁移。
- 桌面集成用隔离 WebView2 数据和合成 Cookie 检查属性保留。
- 发布包通过真正的 ChromeHost 可执行程序和当前用户命名管道测试错误配对拒绝、正确配对转发。该测试不触碰用户 Chrome 数据，不代替真实 Chrome 扩展安装与账号验证。

参考：Chrome Native Messaging、Cookies API 和 WebView2 CookieManager 官方文档。实现采用普通受支持的扩展 API，不需要开放 Chrome 调试端口。
