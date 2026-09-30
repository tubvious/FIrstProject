// Applies the saved (or system) colour theme before first paint to avoid a flash of the wrong theme.
(function () {
  var theme = 'light';
  try {
    var stored = JSON.parse(localStorage.getItem('chess.theme') || 'null');
    theme = stored === 'dark' || stored === 'light'
      ? stored
      : (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  } catch (e) {
    // Storage can be unavailable (privacy mode); fall back to light.
  }
  document.documentElement.setAttribute('data-theme', theme);
})();
