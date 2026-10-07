const button = document.querySelector('#sync');
const status = document.querySelector('#status');
button.addEventListener('click', async () => {
  const pairingCode = document.querySelector('#pairing').value.trim();
  if (!/^[0-9a-f]{32}$/i.test(pairingCode)) { status.textContent = '请输入当前随引窗口的 32 位配对码。'; return; }
  button.disabled = true; status.textContent = '等待随引确认…';
  try {
    const reply = await chrome.runtime.sendMessage({type:'sync-bilibili', pairingCode});
    status.textContent = reply?.message || '没有收到同步结果。';
  } catch { status.textContent = '本机组件未连接，请检查注册状态及随引是否运行。'; }
  finally { button.disabled = false; }
});
