const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
let draw, canvas, tainted = false;
const video = {videoWidth:1920, videoHeight:1080, currentSrc:'test-video', currentTime:12,
  seeking:false, readyState:4, clientWidth:960, clientHeight:540, offsetWidth:960, offsetHeight:540,
  clientLeft:0, clientTop:0, getBoundingClientRect:() => ({left:0,top:0,right:960,bottom:540,width:960,height:540})};
const document = {querySelectorAll:selector => selector === 'video' ? [video] : [],
  createElement:() => canvas = {getContext:() => ({drawImage:(...args) => draw=args}),
    toDataURL:() => { if (tainted) throw Object.assign(new Error(), {name:'SecurityError'}); return 'data:image/png;base64,AA=='; }}};
const window = {addEventListener() {}, chrome:{webview:{postMessage() {}}}}; window.top = window;
vm.runInNewContext(fs.readFileSync(require.resolve('../assets/bridge.js'),'utf8'), {
  window, document, location:{href:'https://example.test/video',hostname:'example.test'},
  crypto:{randomUUID:() => 'test'}, getComputedStyle:() => ({visibility:'visible',display:'block',opacity:'1',objectFit:'contain',objectPosition:'50% 50%'}),
  innerWidth:960,innerHeight:540,setInterval() {},setTimeout() {},clearTimeout() {},addEventListener() {},URL
});
const region = {X:.05,Y:.1,Width:.125,Height:240/1080};
assert.equal(window.guideMate.captureFrame(region).method,'canvas');
assert.equal(canvas.width,160); assert.equal(canvas.height,160);
window.guideMate.captureFrame(region,true);
assert.equal(canvas.width,240); assert.equal(canvas.height,240);
assert.equal(draw[7],240); assert.equal(draw[8],240);
window.guideMate.captureFrame(null,true); assert.equal(canvas.width,1920);
video.seeking = true; assert.equal(window.guideMate.captureFrame(region,true),null); video.seeking = false;
tainted = true; assert.equal(window.guideMate.captureFrame(region,true).method,'preview');
assert.equal(window.guideMate.captureFrame(region).method,'preview');
console.log('PASS map frame bridge: preserved detail, legacy arrow size, seeking, cross-origin fallback (offline fixture)');
