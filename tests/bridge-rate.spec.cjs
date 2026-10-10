const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

// Offline media lifecycle model; this does not open a browser or user profile.
class Video extends EventTarget {
  constructor(source = 'blob:episode-1') {
    super();
    Object.assign(this, {currentSrc:source, currentTime:0, duration:120, paused:false,
      readyState:4, playbackRate:1, defaultPlaybackRate:1, volume:1, muted:false,
      seeking:false, controls:false, textTracks:[], isConnected:true});
  }
  getBoundingClientRect() { return {left:0, top:0, right:640, bottom:360, width:640, height:360}; }
  closest() { return null; }
  setAttribute() {}
  removeAttribute() {}
  load(source) {
    this.currentSrc = source; this.readyState = 0; this.paused = true;
    this.playbackRate = this.defaultPlaybackRate;
    this.dispatchEvent(new Event('loadstart'));
  }
}

let video = new Video(), tick, state;
const styles = new Map();
const document = {querySelectorAll:s => s === 'video' && video ? [video] : [],
  body:{classList:{toggle() {}}}, head:{append:node => styles.set(node.id,node)},
  getElementById:id => styles.get(id), createElement:() => ({remove() {}})};
const window = {chrome:{webview:{postMessage:value => state=value}},
  addEventListener() {}, removeEventListener() {}};
window.top = window;
const location = {hostname:'example.test', href:'https://example.test/video?p=1'};
const bridgePath = process.argv[2] || require.resolve('../assets/bridge.js');
vm.runInNewContext(fs.readFileSync(bridgePath,'utf8'), {
  window, document, location, URL, crypto:{randomUUID:() => 'fixture'},
  getComputedStyle:() => ({visibility:'visible', display:'block', opacity:'1'}),
  innerWidth:640, innerHeight:360, setInterval:fn => tick=fn,
  setTimeout:() => 1, clearTimeout() {}, addEventListener() {}
});
let checks = 0;
function rate(expected, label) {
  tick();
  assert.equal(video.playbackRate, expected, label);
  assert.equal(state.rate, expected, label + ': reported rate matches actual media');
  checks++;
}
function command(action, value) { assert.equal(window.guideMate.command({action,value}), true); }
tick(); command('focusOn'); command('rate', 1.5);
rate(1.5, 'configured speed applies in immersive mode');
video.currentSrc = 'blob:episode-2'; video.playbackRate = 1;
rate(1.5, 'a new episode reusing the video element restores configured speed');
assert.equal(video.defaultPlaybackRate, 1.5, 'native media loads use the configured default'); checks++;
video.load('blob:episode-3');
rate(1.5, 'media loading retains speed before metadata is available');
video.playbackRate = 1; video.readyState = 1;
video.dispatchEvent(new Event('loadedmetadata'));
assert.equal(video.playbackRate, 1.5, 'metadata initialization restores a player reset'); checks++;
video.playbackRate = 1; video.readyState = 4; video.paused = false;
rate(1.5, 'first playable state restores a later player initialization reset');
video.playbackRate = 1.75;
rate(1.75, 'manual website speed changes remain effective after initialization');
video.load('blob:episode-4'); video.readyState = 4; video.paused = false;
rate(1.75, 'a later episode inherits the manual website speed');
video = new Video('blob:episode-5');
rate(1.75, 'a replacement video element inherits configured speed');
command('temporaryRate', 3);
rate(3, 'temporary speed applies without changing the permanent speed');
assert.equal(video.defaultPlaybackRate, 1.75, 'temporary speed does not become the native default'); checks++;
command('rate', 1.75);
rate(1.75, 'releasing temporary speed restores the permanent speed');
command('temporaryRate', 3); video.load('blob:episode-6');
video.readyState = 4; video.paused = false;
rate(1.75, 'media loading cancels temporary speed before the next episode');
command('temporaryRate', 3); location.href = 'https://example.test/video?p=7';
rate(1.75, 'an episode URL change cancels temporary speed even with the same source');
command('rate', 2.5);
rate(2.5, 'a new permanent speed remains user controlled');
video.paused = true; tick(); video.paused = false;
rate(2.5, 'pause and resume keep the selected speed');
video.load('blob:paused-episode'); video.readyState = 4;
rate(2.5, 'paused loaded media restores speed without starting playback');
video.playbackRate = 1.25;
rate(1.25, 'manual website speed changes are respected before first playback');
video.playbackRate = 1; video.paused = false; video.dispatchEvent(new Event('playing'));
rate(1.25, 'first playback restores the latest manual speed after a player reset');
command('rate', 2.5);
const oldVideo = video; video = null; tick();
assert.equal(state.type, 'no-video', 'missing video clears the controlled target'); checks++;
oldVideo.playbackRate = 1; oldVideo.dispatchEvent(new Event('loadedmetadata'));
assert.equal(oldVideo.playbackRate, 1, 'detached targets no longer receive restoration'); checks++;
video = new Video('blob:episode-8'); rate(2.5, 'speed survives a temporary gap without video');
console.log(`PASS ${checks} playback rate bridge checks (offline lifecycle model)`);
