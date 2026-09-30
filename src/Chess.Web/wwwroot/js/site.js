// Shared behaviour for every page: the theme and sound toggles in the header.
import { prefs } from './common/storage.js';
import { sounds } from './common/sound.js';

function initThemeToggle() {
  const toggle = document.getElementById('theme-toggle');
  if (!toggle) return;

  const apply = (theme) => {
    document.documentElement.setAttribute('data-theme', theme);
    toggle.setAttribute('aria-pressed', String(theme === 'dark'));
    toggle.title = theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';
  };

  apply(document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light');
  toggle.addEventListener('click', () => {
    const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    prefs.set('theme', next);
    apply(next);
  });
}

function initSoundToggle() {
  const toggle = document.getElementById('sound-toggle');
  if (!toggle) return;

  const render = () => {
    toggle.setAttribute('aria-pressed', String(sounds.enabled));
    toggle.title = sounds.enabled ? 'Mute sounds' : 'Unmute sounds';
  };

  render();
  toggle.addEventListener('click', () => {
    sounds.enabled = !sounds.enabled;
    render();
    sounds.play('move');
  });
}

initThemeToggle();
initSoundToggle();
