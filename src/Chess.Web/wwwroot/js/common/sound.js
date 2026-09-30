import { prefs } from './storage.js';

/**
 * Subtle game sounds synthesised with the Web Audio API, so no audio files are needed.
 * The audio context is created lazily on the first user gesture (browser autoplay rules).
 */
class SoundBoard {
  #context = null;
  #master = null;
  #noise = null;
  #enabled = prefs.get('sound', true) !== false;

  constructor() {
    const unlock = () => {
      this.#ensureContext();
      window.removeEventListener('pointerdown', unlock);
      window.removeEventListener('keydown', unlock);
    };
    window.addEventListener('pointerdown', unlock);
    window.addEventListener('keydown', unlock);
  }

  get enabled() {
    return this.#enabled;
  }

  set enabled(value) {
    this.#enabled = Boolean(value);
    prefs.set('sound', this.#enabled);
  }

  /** @param {'move'|'capture'|'check'|'gameEnd'|'notify'} kind */
  play(kind) {
    if (!this.#enabled) return;
    const ctx = this.#ensureContext();
    if (!ctx || ctx.state !== 'running') return;

    const now = ctx.currentTime + 0.005;
    switch (kind) {
      case 'move':
        this.#knock(now, 0.9);
        break;
      case 'capture':
        this.#knock(now, 1.0, 330);
        this.#knock(now + 0.045, 0.65, 250);
        break;
      case 'check':
        this.#knock(now, 0.8);
        this.#tone(now + 0.02, 740, 0.09, 0.07, 'triangle');
        this.#tone(now + 0.11, 988, 0.11, 0.07, 'triangle');
        break;
      case 'gameEnd':
        [523.25, 659.25, 783.99, 1046.5].forEach((freq, i) => this.#tone(now + i * 0.09, freq, 0.55, 0.06));
        break;
      case 'notify':
        this.#tone(now, 880, 0.12, 0.05);
        this.#tone(now + 0.1, 1175, 0.16, 0.045);
        break;
    }
  }

  #ensureContext() {
    if (!this.#context) {
      const AudioContextType = window.AudioContext ?? window.webkitAudioContext;
      if (!AudioContextType) return null;
      this.#context = new AudioContextType();
      this.#master = this.#context.createGain();
      this.#master.gain.value = 0.55;
      this.#master.connect(this.#context.destination);
    }
    if (this.#context.state === 'suspended') {
      this.#context.resume().catch(() => {});
    }
    return this.#context;
  }

  /** A short wooden "tock": a quickly decaying low tone plus a filtered noise transient. */
  #knock(start, intensity, pitch = 420) {
    const ctx = this.#context;

    const osc = ctx.createOscillator();
    const oscGain = ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(pitch, start);
    osc.frequency.exponentialRampToValueAtTime(pitch * 0.55, start + 0.08);
    oscGain.gain.setValueAtTime(0.0001, start);
    oscGain.gain.exponentialRampToValueAtTime(0.32 * intensity, start + 0.004);
    oscGain.gain.exponentialRampToValueAtTime(0.0001, start + 0.11);
    osc.connect(oscGain).connect(this.#master);
    osc.start(start);
    osc.stop(start + 0.12);

    const noise = ctx.createBufferSource();
    noise.buffer = this.#noiseBuffer();
    const filter = ctx.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.value = 2400;
    filter.Q.value = 0.9;
    const noiseGain = ctx.createGain();
    noiseGain.gain.setValueAtTime(0.18 * intensity, start);
    noiseGain.gain.exponentialRampToValueAtTime(0.0001, start + 0.035);
    noise.connect(filter).connect(noiseGain).connect(this.#master);
    noise.start(start);
    noise.stop(start + 0.04);
  }

  #tone(start, frequency, duration, volume, type = 'sine') {
    const ctx = this.#context;
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = type;
    osc.frequency.value = frequency;
    gain.gain.setValueAtTime(0.0001, start);
    gain.gain.exponentialRampToValueAtTime(volume, start + 0.012);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + duration);
    osc.connect(gain).connect(this.#master);
    osc.start(start);
    osc.stop(start + duration + 0.02);
  }

  #noiseBuffer() {
    if (!this.#noise) {
      const ctx = this.#context;
      const buffer = ctx.createBuffer(1, Math.floor(ctx.sampleRate * 0.05), ctx.sampleRate);
      const data = buffer.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      this.#noise = buffer;
    }
    return this.#noise;
  }
}

export const sounds = new SoundBoard();
