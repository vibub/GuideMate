chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id || message.type !== 'sync-bilibili' || !/^[0-9a-f]{32}$/i.test(message.pairingCode)) return false;
  (async () => {
    const all = await chrome.cookies.getAll({domain:'bilibili.com'});
    const allowed = all.filter(cookie => {
      const domain = cookie.domain.replace(/^\./, '');
      return (domain === 'bilibili.com' || domain.endsWith('.bilibili.com'))
        && (cookie.session || cookie.expirationDate > Date.now() / 1000);
    });
    const cookies = allowed.filter(cookie => !cookie.partitionKey).map(cookie => ({
      name:cookie.name, value:cookie.value, domain:cookie.domain, path:cookie.path,
      secure:cookie.secure, httpOnly:cookie.httpOnly, session:cookie.session,
      sameSite:cookie.sameSite, expirationDate:cookie.expirationDate
    }));
    if (!cookies.some(cookie => cookie.name === 'SESSDATA')) {
      sendResponse({success:false, message:'当前 Chrome 配置未发现 B站登录会话，请先在 Chrome 登录 B站。'}); return;
    }
    const reply = await chrome.runtime.sendNativeMessage('com.guidemate.bilibili', {
      type:'import-bilibili', pairingCode:message.pairingCode, cookies,
      skippedPartitioned:allowed.length - cookies.length
    });
    sendResponse(reply);
  })().catch(() => sendResponse({success:false, message:'本机同步组件未连接。请打开随引并检查组件注册。'}));
  return true;
});
