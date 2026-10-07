const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Run the shipped bridge against a DOM model. No browser, WPF window or real
// user profile is opened. The classes match Bilibili's public player bundle.
class Element {
  constructor(className = '', children = [], nodeName = 'DIV') {
    this.nodeType = 1;
    this.nodeName = nodeName;
    this.className = className;
    this.childNodes = [];
    this.attributes = new Map();
    this.style = {};
    this.hidden = false;
    this.isConnected = true;
    this.classList = {toggle: (name, enabled) => {
      const classes = new Set(this.className.split(/\s+/).filter(Boolean));
      if (enabled) classes.add(name); else classes.delete(name);
      this.className = [...classes].join(' ');
    }};
    children.forEach(child => this.append(child));
  }
  append(child) {
    if (typeof child === 'string') child = {nodeType: 3, nodeName: '#text', textContent: child};
    child.parentElement = this;
    this.childNodes.push(child);
    return child;
  }
  matches(selectors) {
    return selectors.split(',').some(selector => {
      selector = selector.trim();
      if (selector.startsWith('.')) return this.className.split(/\s+/).includes(selector.slice(1));
      if (selector.startsWith('#')) return this.id === selector.slice(1);
      if (selector.startsWith('[')) return this.attributes.has(selector.slice(1, -1));
      return this.nodeName.toLowerCase() === selector;
    });
  }
  querySelectorAll(selector) {
    return this.childNodes.filter(child => child.nodeType === 1)
      .flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]);
  }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  closest(selector) {
    for (let node = this; node; node = node.parentElement) if (node.matches(selector)) return node;
    return null;
  }
  getBoundingClientRect() {
    let hidden = false;
    for (let node = this; node; node = node.parentElement) hidden ||= node.hidden || node.style.display === 'none';
    return {left: 0, top: 0, right: 1280, bottom: hidden ? 0 : 720, width: 1280, height: hidden ? 0 : 720};
  }
  setAttribute(name, value) { this.attributes.set(name, value); }
  removeAttribute(name) { this.attributes.delete(name); }
  remove() {
    this.parentElement.childNodes = this.parentElement.childNodes.filter(child => child !== this);
    this.isConnected = false;
  }
}

const body = new Element(), head = new Element();
const document = {
  body, head,
  querySelectorAll: selector => body.querySelectorAll(selector),
  getElementById: id => head.querySelector('#' + id),
  createElement: tag => new Element('', [], tag.toUpperCase())
};
const video = new Element('', [], 'VIDEO');
Object.assign(video, {textTracks: [], currentTime: 159, currentSrc: 'blob:synthetic-video',
  duration: 224, paused: true, playbackRate: 1, volume: 1, muted: false, seeking: false, controls: false});
const player = body.append(new Element('bpx-player-container', [video]));
let tick, state;
const window = {chrome: {webview: {postMessage: message => {state = message;}}},
  addEventListener() {}, removeEventListener() {}};
window.top = window;
const getComputedStyle = element => {
  let visibility = element.style.visibility;
  for (let node = element.parentElement; !visibility && node; node = node.parentElement) visibility = node.style.visibility;
  if (body.className.includes('guidemate-video-focus') && !element.attributes.has('data-guidemate-target')) visibility = 'hidden';
  return {display: 'block', opacity: '1', visibility: visibility || 'visible', ...element.style};
};
const bridgePath = process.argv[2] ? path.resolve(process.argv[2]) : path.join(__dirname, '../assets/bridge.js');
vm.runInNewContext(fs.readFileSync(bridgePath, 'utf8'), {
  window, document, getComputedStyle, crypto: {randomUUID: () => 'synthetic-frame'},
  innerWidth: 1280, innerHeight: 720, location: {href: 'https://www.bilibili.com/video/BV14Z421L7DN/?p=3'},
  addEventListener() {}, setInterval: callback => {tick = callback;}, setTimeout: () => 1, clearTimeout() {}
});

let passed = 0;
function expect(expected, label) {
  tick();
  assert.equal(state.type, 'state', label + ': video state');
  assert.equal(state.subtitle, expected, label);
  passed++;
}
function caption(className, children, parent = player) { return parent.append(new Element(className, children)); }
function clearCaptions() { player.childNodes = [video]; video.textTracks = []; }

const sentence = '来东北方向地图上角';
const modern = caption('bili-subtitle-x-subtitle-panel-text', [sentence]);
caption('bpx-player-ctrl-subtitle', ['字幕', '关闭', '中文 AI', '字幕设置']);
expect(sentence, 'modern Bilibili AI caption excludes the subtitle menu');
modern.append(new Element('bili-subtitle-x-ai-badge', ['AI']));
modern.append(new Element('bili-subtitle-x-subtitle-panel-icon', ['AI']));
expect(sentence, 'caption excludes embedded AI badges and icons');
modern.childNodes = ['向北走', new Element('', [], 'BR'), new Element('', ['然后右转'], 'SPAN')]
  .map(child => typeof child === 'string' ? {nodeType: 3, nodeName: '#text', textContent: child} : child);
expect('向北走\n然后右转', 'explicit line breaks and nested spans are preserved');
caption('bili-subtitle-x-subtitle-panel-text', ['Go north, then turn right']);
expect('向北走\n然后右转\nGo north, then turn right', 'major and minor bilingual lines');
caption('bili-subtitle-x-subtitle-panel-text', ['Go north, then turn right']);
expect('向北走\n然后右转\nGo north, then turn right', 'duplicate rendering is not repeated');

clearCaptions();
caption('bili-subtitle-x-subtitle-rawmeat-text', ['  沿着道路\u00a0向西走  ']);
expect('沿着道路 向西走', 'current rawmeat renderer and whitespace');
clearCaptions();
const group = caption('bili-subtitle-x-subtitle-panel-major-group', []);
const line = caption('bili-subtitle-x-subtitle-panel-text', [sentence], group);
expect(sentence, 'visible caption');
line.style.opacity = '0';
expect('', 'transparent caption is excluded');
line.style = {}; group.style.opacity = '0';
expect('', 'transparent ancestor is excluded');
group.style = {display: 'none'};
expect('', 'closed subtitle panel does not retain old text');
group.style = {}; group.hidden = true;
expect('', 'hidden ancestor is excluded');
group.hidden = false; group.style.visibility = 'hidden';
expect('', 'site-hidden subtitle is excluded in normal mode');
group.style = {}; line.style.visibility = 'collapse';
expect('', 'collapsed caption is excluded');
line.style = {};
window.guideMate.command({action: 'focusOn'});
expect(sentence, 'video-only focus keeps subtitle synchronization');
group.style.display = 'none';
expect('', 'closed subtitles stay cleared in video-only focus');
group.style = {};
window.guideMate.command({action: 'focusOff'});
expect(sentence, 'caption synchronization after leaving focus');
video.seeking = true;
expect('', 'seek does not send the previous caption');
video.seeking = false; line.childNodes[0].textContent = '沿山壁向南走'; video.currentTime = 200;
expect('沿山壁向南走', 'new caption after seek while paused');
clearCaptions();
expect('', 'caption gap and media changes clear old text');

body.append(new Element('bpx-player-container', [new Element('bili-subtitle-x-subtitle-panel-text', ['其他视频的字幕'])]));
expect('', 'subtitles from another player are excluded');
caption('bpx-player-subtitle-panel-text', ['旧版 B 站字幕']);
expect('旧版 B 站字幕', 'legacy bpx player remains supported');
caption('bili-subtitle-x-subtitle-panel-text', ['新版当前字幕']);
expect('新版当前字幕', 'current renderer takes priority over a leftover legacy node');
clearCaptions();
caption('bilibili-player-video-subtitle', ['老播放器字幕']);
expect('老播放器字幕', 'original Bilibili player remains supported');
clearCaptions();
caption('subtitle-item-text', ['通用网页字幕']);
expect('通用网页字幕', 'generic subtitle adapter remains supported');
clearCaptions();
caption('ytp-caption-segment', ['Turn left']);
expect('Turn left', 'YouTube caption adapter remains supported');
video.textTracks = [{kind: 'subtitles', mode: 'showing', activeCues: [{text: '原生字幕'}]}];
expect('原生字幕', 'enabled native subtitle takes priority');
video.textTracks[0].mode = 'disabled';
expect('Turn left', 'disabled native track falls back to DOM captions');
video.textTracks[0] = {kind: 'metadata', mode: 'showing', activeCues: [{text: 'not a caption'}]};
expect('Turn left', 'metadata track is not mistaken for a caption');
video.textTracks[0] = {kind: 'captions', mode: 'showing', activeCues: [{text: '  '} ]};
expect('Turn left', 'empty native track falls back to DOM captions');
video.textTracks[0].activeCues = [{text: 'captions track'}];
expect('captions track', 'native captions track remains supported');

console.log(`PASS ${passed} webpage subtitle bridge checks; no UI or user data opened`);
