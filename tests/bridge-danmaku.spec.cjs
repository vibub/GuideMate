const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

// Same post-filter model fields as Bilibili nano 4.10.4 (2026-09-20).
// These are offline models, not evidence of real-site playback.
let tick, state, seeking;
const video = {currentTime:10, currentSrc:'blob:fixture', duration:40, paused:false, playbackRate:1,
  volume:1, muted:false, seeking:false, textTracks:[], controls:false,
  getBoundingClientRect:() => ({left:0,top:0,right:640,bottom:360,width:640,height:360}),
  closest:() => null, setAttribute() {}, removeAttribute() {},
  addEventListener:(type, handler) => { if (type === 'seeking') seeking = handler; }, removeEventListener() {}};
const styles = new Map();
const document = {querySelectorAll:s => s === 'video' ? [video] : [],
  body:{classList:{toggle() {}}}, head:{append:node => styles.set(node.id,node)},
  getElementById:id => styles.get(id), createElement:() => ({remove() {}})};
const renderer = {visible:true,manager:{renderTime:10,visualArray:[]}};
const window = {chrome:{webview:{postMessage:value => state=value}}, addEventListener() {},removeEventListener() {},
  player:{danmaku:{getDanmakuX:() => renderer,isOpen:() => renderer.visible}}};
window.top = window;
const location = {hostname:'www.bilibili.com',href:'https://www.bilibili.com/video/BV14Z421L7DN/'};
vm.runInNewContext(fs.readFileSync(require.resolve('../assets/bridge.js'),'utf8'), {
  window,document,location,crypto:{randomUUID:() => 'fixture'},
  getComputedStyle:() => ({visibility:'visible',display:'block',opacity:'1'}),
  innerWidth:640,innerHeight:360,setInterval:fn => tick=fn,setTimeout:() => 1,clearTimeout() {},addEventListener() {}
});
let checks=0;
function check(test,label) { assert.ok(test,label); checks++; }
function model(id,mode=1,extra={}) {
  return {showed:true,showTime:9,textData:{dmid:id,stime:9,text:'测试弹幕 '+id,color:0x123456,size:25,mode,...extra}};
}
renderer.manager.visualArray=[model('a'),model('top',5),model('bottom',4),model('reverse',6),
  {...model('blocked'),isHide:true},{...model('future'),showed:false},model('script',7),
  model('special',2000),model('empty',1,{text:''}),model('long',1,{text:'x'.repeat(201)})];
tick();
check(state.danmaku.available && state.danmaku.enabled,'current player exposes a usable source');
assert.deepEqual(Array.from(state.danmaku.items,item => item.id),['a:9','top:9','bottom:9','reverse:9']);
check(state.danmaku.items[0].color===0x123456 && state.danmaku.items[0].offset===1,'color and elapsed source time');
const id=state.danmaku.items[0].id; tick();
check(state.danmaku.items[0].id===id,'redrawn models retain an identity for host deduplication');
window.guideMate.command({action:'focusOn'}); tick();
check(state.danmaku.items.length===4,'video-only focus continues extracting hidden native danmaku');
window.guideMate.command({action:'focusOff'});
renderer.visible=false; tick();
check(!state.danmaku.enabled && state.danmaku.items.length===0,'Bilibili DM switch is respected');
renderer.visible=true; video.seeking=true; const epoch=state.danmaku.epoch; seeking(); tick();
check(state.danmaku.items.length===0 && state.danmaku.epoch!==epoch,'seeking clears comments and advances generation even for a small seek');
video.seeking=false;
renderer.manager.visualArray=[model('raw',2000,{rawMode:1,text:'原始普通弹幕'})]; tick();
check(state.danmaku.items[0].mode===1,'special decoration can retain its ordinary raw mode');
renderer.manager.visualArray=Array.from({length:300},(_,i) => model(String(i))); tick();
check(state.danmaku.items.length===240,'message batches are bounded');
window.player=null; tick();
check(!state.danmaku.available && !state.danmaku.enabled,'unavailable renderer does not report success');
location.hostname='example.com'; tick();
check(state.danmaku===null,'other sites do not expose a Bilibili source');
location.hostname='bilibili.com'; window.player={danmaku:{getDanmakuX:() => renderer}}; tick();
check(state.danmaku.enabled,'apex Bilibili hostname and optional site switch');
console.log('PASS '+checks+' danmaku bridge checks (offline models)');
