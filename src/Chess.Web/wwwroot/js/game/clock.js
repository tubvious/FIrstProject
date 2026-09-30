const TICK_MS = 100;

/** Formats milliseconds as h:mm:ss, m:ss, or m:ss.t when under ten seconds. */
export function formatClock(ms) {
  const clamped = Math.max(0, ms);
  const totalSeconds = Math.floor(clamped / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n) => String(n).padStart(2, '0');

  if (hours > 0) return `${hours}:${pad(minutes)}:${pad(seconds)}`;
  if (clamped < 10_000) return `${minutes}:${pad(seconds)}.${Math.floor((clamped % 1000) / 100)}`;
  return `${minutes}:${pad(seconds)}`;
}

/**
 * Counts the running clock down locally between server snapshots. The server remains the only
 * authority on time: every snapshot resets the local reading, and flag falls are decided server-side.
 */
export class ClockTicker {
  #clock = null;
  #syncedAt = 0;
  #timer = null;
  #onTick;

  constructor(onTick) {
    this.#onTick = onTick;
  }

  /** @param {{ whiteMs: number, blackMs: number, running: 'white'|'black'|null } | null} clock */
  sync(clock) {
    this.#clock = clock;
    this.#syncedAt = performance.now();
    clearTimeout(this.#timer);
    this.#timer = null;
    this.#onTick();
    if (clock?.running) this.#schedule();
  }

  remaining(color) {
    if (!this.#clock) return 0;
    const base = color === 'white' ? this.#clock.whiteMs : this.#clock.blackMs;
    const elapsed = this.#clock.running === color ? performance.now() - this.#syncedAt : 0;
    return Math.max(0, base - elapsed);
  }

  #schedule() {
    this.#timer = setTimeout(() => {
      this.#onTick();
      if (this.#clock?.running) this.#schedule();
    }, TICK_MS);
  }
}
