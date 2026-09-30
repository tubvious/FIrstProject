// Safe wrappers around Web Storage (which can be unavailable in private mode or when disabled).

const PREFIX = 'chess.';
const MAX_REMEMBERED_GAMES = 50;

function open(name) {
  try {
    const storage = window[name];
    const probe = `${PREFIX}probe`;
    storage.setItem(probe, probe);
    storage.removeItem(probe);
    return storage;
  } catch {
    return null;
  }
}

const local = open('localStorage');
const session = open('sessionStorage');

function read(storage, key, fallback) {
  try {
    const raw = storage?.getItem(PREFIX + key);
    return raw == null ? fallback : JSON.parse(raw);
  } catch {
    return fallback;
  }
}

function write(storage, key, value) {
  try {
    storage?.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    // Quota exceeded or storage disabled: preferences are a convenience, so ignore.
  }
}

/** Persistent per-browser preferences (theme, sound, player name). */
export const prefs = {
  get: (key, fallback) => read(local, key, fallback),
  set: (key, value) => write(local, key, value),
};

/**
 * Seat tokens prove which seat this browser owns in a game.
 * - The tab's own token lives in sessionStorage, so two tabs of the same browser can be two players.
 * - Every token is also remembered in localStorage, so closing the tab and reopening the link resumes the seat.
 */
export const seats = {
  tabToken(code) {
    return read(session, `seat.${code}`, null);
  },

  knownTokens(code) {
    const games = read(local, 'seats', {});
    return games[code]?.tokens ?? [];
  },

  remember(code, token) {
    if (!token) return;
    write(session, `seat.${code}`, token);

    const games = read(local, 'seats', {});
    const tokens = new Set(games[code]?.tokens ?? []);
    tokens.add(token);
    games[code] = { tokens: [...tokens].slice(-4), at: Date.now() };

    // Keep only the most recently used games.
    const recent = Object.entries(games)
      .sort(([, a], [, b]) => (b.at ?? 0) - (a.at ?? 0))
      .slice(0, MAX_REMEMBERED_GAMES);
    write(local, 'seats', Object.fromEntries(recent));
  },
};
