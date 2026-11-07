(() => {
  let blocked = true;
  const allow = () => { blocked = false; };
  window.guideMateAllowStartupPlayback = allow;
  // Install before site scripts: block scripted play, including delayed autoplay.
  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function (...args) {
    return blocked ? Promise.resolve() : play.apply(this, args);
  };
  // Native autoplay does not call the JavaScript play method.
  document.addEventListener('play', event => {
    if (blocked && event.target instanceof HTMLMediaElement) event.target.pause();
  }, true);
  const interaction = event => { if (event.isTrusted) allow(); };
  document.addEventListener('pointerdown', interaction, true);
  document.addEventListener('keydown', interaction, true);
})();
