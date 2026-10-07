(() => {
  if (window.guideMate) return;
  let preferredRate = null;
  let target = null;
  let focusEnabled = false;
  let focusedControls = null;
  let controlsTimer = null;
  let controlsPointerDown = false;
  let frameVisibility = window === window.top ? 1 : 0;
  let frameViewport = null;
  let blockedCanvasKey = null;
  const frameToken = crypto.randomUUID();
  const post = value => window.chrome?.webview?.postMessage(value);
  const area = element => {
    const r = element.getBoundingClientRect(), style = getComputedStyle(element);
    if (style.visibility === 'hidden' || style.display === 'none' || style.opacity === '0') return 0;
    return Math.max(0, Math.min(r.right, innerWidth) - Math.max(r.left, 0))
      * Math.max(0, Math.min(r.bottom, innerHeight) - Math.max(r.top, 0)) * frameVisibility;
  };
  const visibleVideo = () => [...document.querySelectorAll('video')]
    .filter(video => area(video) > 400).sort((a, b) => area(b) - area(a))[0];
  const finite = value => Number.isFinite(value) ? value : 0;
  const viewport = () => window === window.top
    ? {x:0, y:0, sx:1, sy:1, width:innerWidth, height:innerHeight} : frameViewport;
  const frameInfo = () => {
    const video = visibleVideo(), view = viewport();
    if (!video || !view || video.readyState < 2 || !video.videoWidth || !video.videoHeight) return null;
    const r = video.getBoundingClientRect(), style = getComputedStyle(video);
    // Account for letterboxing and object-fit before mapping intrinsic pixels to the WebView preview.
    const bx = r.width / Math.max(1, video.offsetWidth), by = r.height / Math.max(1, video.offsetHeight);
    const boxWidth = video.clientWidth * bx, boxHeight = video.clientHeight * by;
    const fit = style.objectFit;
    let width = boxWidth, height = boxHeight;
    if (fit !== 'fill') {
      let scale = fit === 'cover' ? Math.max(boxWidth / video.videoWidth, boxHeight / video.videoHeight)
        : Math.min(boxWidth / video.videoWidth, boxHeight / video.videoHeight);
      if (fit === 'none') scale = 1;
      if (fit === 'scale-down') scale = Math.min(1, scale);
      width = video.videoWidth * scale; height = video.videoHeight * scale;
    }
    const position = style.objectPosition.split(/\s+/);
    const offset = (value, space) => value?.endsWith('%') ? space * parseFloat(value) / 100
      : value?.endsWith('px') ? parseFloat(value) : space / 2;
    const x = r.left + video.clientLeft * bx + offset(position[0], boxWidth - width);
    const y = r.top + video.clientTop * by + offset(position[1], boxHeight - height);
    return {mediaKey:location.href + '|' + video.currentSrc, time:finite(video.currentTime),
      width:video.videoWidth, height:video.videoHeight, seeking:video.seeking,
      x:view.x + x * view.sx, y:view.y + y * view.sy, displayWidth:width * view.sx, displayHeight:height * view.sy,
      viewportWidth:view.width, viewportHeight:view.height};
  };
  const captureFrame = region => {
    const info = frameInfo(), video = visibleVideo();
    if (!info || info.seeking) return null;
    if (blockedCanvasKey === info.mediaKey) return {...info, method:'preview'};
    const canvas = document.createElement('canvas');
    const scale = Math.min(1, 1280 / info.width);
    canvas.width = region ? 160 : Math.round(info.width * scale);
    canvas.height = region ? 160 : Math.round(info.height * scale);
    const context = canvas.getContext('2d');
    if (region) context.drawImage(video, region.X * info.width, region.Y * info.height,
      region.Width * info.width, region.Height * info.height, 0, 0, 160, 160);
    else context.drawImage(video, 0, 0, canvas.width, canvas.height);
    try { return {...info, method:'canvas', data:canvas.toDataURL('image/png')}; }
    catch (error) {
      if (error.name !== 'SecurityError') throw error;
      blockedCanvasKey = info.mediaKey;
      return {...info, method:'preview'};
    }
  };
  const captionText = node => {
    if (node.nodeType === 3) return node.textContent || '';
    if (node.nodeName === 'BR') return '\n';
    // The AI badge belongs to the player UI, not the spoken caption.
    if (node.matches?.('.bili-subtitle-x-ai-badge, .bili-subtitle-x-subtitle-panel-icon, .bpx-player-subtitle-panel-icon')) return '';
    return [...node.childNodes].map(captionText).join('');
  };
  const captionVisible = element => {
    if (!element.getBoundingClientRect().height) return false;
    for (let node = element; node; node = node.parentElement) {
      const style = getComputedStyle(node);
      if (node.hidden || style.display === 'none' || style.opacity === '0') return false;
      // Our video-only focus CSS hides the website's subtitle layer, but its
      // current text must still reach the independent floating window.
      if (!focusEnabled && (style.visibility === 'hidden' || style.visibility === 'collapse')) return false;
    }
    return true;
  };
  const subtitle = video => {
    if (video.seeking) return '';
    for (const track of video.textTracks || []) {
      if (track.mode !== 'showing' || !['subtitles', 'captions'].includes(track.kind) || !track.activeCues?.length) continue;
      const text = [...track.activeCues].map(cue => cue.text?.trim()).filter(Boolean).join('\n');
      if (text) return text;
    }
    const root = video.closest('.bpx-player-container, .bilibili-player, #bilibili-player, .html5-video-player') || document;
    for (const selector of [
      '.bili-subtitle-x-subtitle-panel-text, .bili-subtitle-x-subtitle-rawmeat-text',
      '.bpx-player-subtitle-panel-text', '.bilibili-player-video-subtitle', '.subtitle-item-text', '.ytp-caption-segment'
    ]) {
      const text = [...root.querySelectorAll(selector)].filter(captionVisible)
        .map(element => captionText(element).replace(/\u00a0/g, ' ').trim()).filter(Boolean);
      if (text.length) return [...new Set(text)].join('\n');
    }
    return '';
  };
  let danmakuVideo = null, danmakuEpoch = 0;
  const danmakuSeek = () => { danmakuEpoch++; };
  const danmaku = video => {
    const host = location.hostname || '';
    if (host !== 'bilibili.com' && !host.endsWith('.bilibili.com')) return null;
    // The current Bilibili player exposes its post-filter render models. Reading
    // these avoids intercepting canvas calls or bypassing the user's DM filters.
    if (danmakuVideo !== video) {
      danmakuVideo?.removeEventListener?.('seeking', danmakuSeek);
      danmakuVideo = video; danmakuEpoch++;
      video.addEventListener?.('seeking', danmakuSeek);
    }
    const api = window.player?.danmaku;
    const renderer = typeof api?.getDanmakuX === 'function' ? api.getDanmakuX() : null;
    const models = renderer?.manager?.visualArray;
    if (!Array.isArray(models)) return {available:false, enabled:false, items:[]};
    const enabled = renderer.visible !== false && (typeof api.isOpen !== 'function' || api.isOpen());
    const items = [];
    if (enabled && !video.seeking) for (const model of models) {
      const data = model.textData;
      if (!data || model.isHide || model.showed === false || typeof data.text !== 'string') continue;
      const mode = Number(data.rawMode ?? data.mode);
      if (![1, 2, 3, 4, 5, 6].includes(mode)) continue; // Exclude scripts, ads and special effects.
      const text = data.text.replace(/[\r\n]+/g, ' ').trim();
      if (!text || text.length > 200 || data.dmid == null) continue;
      items.push({id:String(data.dmid) + ':' + finite(data.stime), text, mode,
        color:Number.isFinite(data.color) ? Math.max(0, Math.min(0xffffff, data.color)) : 0xffffff,
        size:Number.isFinite(data.size) ? Math.max(18, Math.min(36, data.size)) : 25,
        offset:Math.max(0, Math.min(4, finite(renderer.manager.renderTime) - finite(model.showTime)))});
      if (items.length >= 240) break;
    }
    return {available:true, enabled:!!enabled, epoch:frameToken + ':' + danmakuEpoch, items};
  };
  const hideControls = () => {
    if (!focusedControls) return;
    if (controlsPointerDown || focusedControls.video.seeking) {
      controlsTimer = setTimeout(hideControls, 1800); return;
    }
    focusedControls.video.controls = false;
  };
  const showControls = () => {
    if (!focusedControls) return;
    clearTimeout(controlsTimer);
    focusedControls.video.controls = focusedControls.original;
    controlsTimer = setTimeout(hideControls, 1800);
  };
  const controlsActivity = event => {
    if (event.type === 'pointerdown') controlsPointerDown = true;
    if (event.type === 'pointerup' || event.type === 'pointercancel' || event.type === 'blur') controlsPointerDown = false;
    showControls();
  };
  const controlsEvents = ['pointermove', 'pointerdown', 'pointerup', 'pointercancel', 'keydown', 'blur'];
  const restoreControls = () => {
    clearTimeout(controlsTimer);
    if (focusedControls) focusedControls.video.controls = focusedControls.original;
    focusedControls = null; controlsPointerDown = false;
    for (const event of controlsEvents) window.removeEventListener(event, controlsActivity, true);
  };
  const focusControls = video => {
    if (focusedControls?.video === video) return;
    restoreControls();
    focusedControls = {video, original:video.controls};
    for (const event of controlsEvents) window.addEventListener(event, controlsActivity, true);
    showControls();
  };
  const focusElement = (element, enabled) => {
    document.querySelectorAll('[data-guidemate-target]').forEach(el => el.removeAttribute('data-guidemate-target'));
    if (element && enabled) element.setAttribute('data-guidemate-target', 'true');
    document.body?.classList.toggle('guidemate-video-focus', enabled);
    let style = document.getElementById('guidemate-focus-style');
    if (enabled && !style) {
      style = document.createElement('style'); style.id = 'guidemate-focus-style';
      style.textContent = 'body.guidemate-video-focus {overflow:hidden!important;background:#000!important;cursor:default!important} body.guidemate-video-focus * {visibility:hidden!important} body.guidemate-video-focus [data-guidemate-target] {visibility:visible!important;position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;z-index:2147483647!important;object-fit:contain!important;background:#000!important;cursor:default!important;border:0!important}';
      document.head.append(style);
    }
    if (!enabled) style?.remove();
    if (window !== window.top) parent.postMessage({kind:'guidemate-parent-focus', token:frameToken, enabled}, '*');
  };
  const setFocus = enabled => {
    const video = visibleVideo() || (target?.isConnected ? target : null);
    if (enabled && !video) return false;
    if (enabled) focusControls(video); else restoreControls();
    focusEnabled = enabled; focusElement(video, enabled);
    return true;
  };
  // Cross-origin children cannot inspect their iframe; each parent reports the visible fraction.
  addEventListener('message', event => {
    const message = event.data;
    if (!message || typeof message !== 'object') return;
    if (event.source === parent && message.kind === 'guidemate-frame-visible' && message.token === frameToken) {
      frameVisibility = Number.isFinite(message.fraction) ? Math.max(0, Math.min(1, message.fraction)) : 0;
      frameViewport = message.viewport || null;
      return;
    }
    if (!['guidemate-frame-query', 'guidemate-parent-focus'].includes(message.kind)) return;
    const frame = [...document.querySelectorAll('iframe')].find(el => el.contentWindow === event.source);
    if (!frame) return;
    if (message.kind === 'guidemate-frame-query') {
      const r = frame.getBoundingClientRect();
      const view = viewport(), sx = r.width / Math.max(1, frame.offsetWidth), sy = r.height / Math.max(1, frame.offsetHeight);
      event.source.postMessage({kind:'guidemate-frame-visible', token:message.token,
        fraction:area(frame) / Math.max(1, r.width * r.height),
        viewport:view && message.width > 0 && message.height > 0 ? {
          x:view.x + (r.left + frame.clientLeft * sx) * view.sx,
          y:view.y + (r.top + frame.clientTop * sy) * view.sy,
          sx:view.sx * frame.clientWidth * sx / message.width,
          sy:view.sy * frame.clientHeight * sy / message.height, width:view.width, height:view.height} : null}, '*');
    } else focusElement(frame, message.enabled === true);
  });
  const episode = next => {
    const bilibili = location.hostname === 'bilibili.com' || location.hostname.endsWith('.bilibili.com');
    const youtube = location.hostname === 'youtube.com' || location.hostname === 'www.youtube.com';
    const selectors = bilibili ? ['.video-pod__list .video-pod__item', '.video-sections-content-list .video-episode-card', '.multi-page .list-box li', '.video-section-list .video-episode-card']
      : youtube ? ['#playlist-items ytd-playlist-panel-video-renderer'] : ['[data-guidemate-episode]'];
    for (const selector of selectors) {
      // Bilibili's episode list can be collapsed or hidden while the video is focused.
      // Its DOM order and selected marker still identify the adjacent episode.
      const items = [...document.querySelectorAll(selector)].filter(el => bilibili || el.getBoundingClientRect().height > 0);
      if (!items.length) continue;
      const current = items.findIndex(el => el.matches('.on,.active,.playing,[selected],[aria-current="true"],[aria-current="page"]')
        || el.querySelector('.video-episode-card__info-playing,.playing'));
      if (current < 0) continue;
      const index = current + (next ? 1 : -1);
      const item = items[index];
      if (!item || item.matches('[disabled],[aria-disabled="true"]')) return false;
      if (bilibili && item.closest('.video-pod__list.multip')) {
        const url = new URL(location.href);
        url.searchParams.set('p', String(index + 1));
        url.searchParams.delete('t');
        location.assign(url.href); return true;
      }
      (item.querySelector('a') || item).click(); return true;
    }
    const selector = youtube ? (next ? '.ytp-next-button' : '.ytp-prev-button') : (next ? '[rel="next"]' : '[rel="prev"]');
    const button = document.querySelector(selector);
    if (!button || button.matches('[disabled],[aria-disabled="true"]') || !button.getBoundingClientRect().height) return false;
    button.click(); return true;
  };
  window.guideMate = {
    frameInfo,
    captureFrame,
    command(request) {
      if (request.action === 'nextEpisode') return episode(true);
      if (request.action === 'previousEpisode') return episode(false);
      const video = visibleVideo() || (target?.isConnected ? target : null);
      if (!video) return false;
      const value = Number(request.value);
      switch (request.action) {
        case 'play': video.play().catch(error => post({ type: 'error', message: error.message })); break;
        case 'pause': video.pause(); break;
        case 'toggle': if (video.paused) video.play().catch(error => post({ type: 'error', message: error.message })); else video.pause(); break;
        case 'seek': video.currentTime = Math.max(0, Math.min(finite(video.duration) || Number.MAX_SAFE_INTEGER, video.currentTime + value)); break;
        case 'position': video.currentTime = Math.max(0, Math.min(finite(video.duration) || Number.MAX_SAFE_INTEGER, value)); break;
        case 'rate': preferredRate = Math.max(0.25, Math.min(4, value)); video.playbackRate = preferredRate; break;
        case 'volume': video.volume = Math.max(0, Math.min(1, value)); break;
        case 'mute': video.muted = !video.muted; break;
        case 'focus': return setFocus(!focusEnabled);
        case 'focusOn': return setFocus(true);
        case 'focusOff': return setFocus(false);
        default: return false;
      }
      return true;
    }
  };
  setInterval(() => {
    if (window !== window.top) parent.postMessage({kind:'guidemate-frame-query', token:frameToken, width:innerWidth, height:innerHeight}, '*');
    const video = visibleVideo();
    if (!video) { target = null; restoreControls(); post({type:'no-video'}); return; }
    if (target !== video) {
      target = video;
      if (preferredRate !== null) video.playbackRate = preferredRate;
      if (focusEnabled) setFocus(true);
    }
    post({ type: 'state', area:area(video), mediaKey:location.href + '|' + video.currentSrc, time: finite(video.currentTime), duration: finite(video.duration), paused: video.paused,
      rate: video.playbackRate, volume: video.volume, muted: video.muted, subtitle: subtitle(video), danmaku:danmaku(video),
      width:video.videoWidth, height:video.videoHeight, seeking:video.seeking });
  }, 250);
})();
