import { enhanceDialog } from '../common/ui.js';
import { describeOutcome } from './status.js';

const CONFETTI_COLORS = ['#3f6fe8', '#1f9d61', '#f0b43c', '#dc3b3f', '#8b5cf6', '#14b8a6'];

/** The end-of-game card with the result, Rematch and Back to Home. */
export class ResultDialog {
  #dialog;
  #card;
  #els;

  constructor(dialog, { onRematch }) {
    this.#dialog = enhanceDialog(dialog);
    this.#card = dialog.querySelector('.result');
    const find = (role) => dialog.querySelector(`[data-role="${role}"]`);
    this.#els = {
      emblem: find('emblem'),
      kicker: find('kicker'),
      title: find('title'),
      detail: find('detail'),
      note: find('note'),
      rematch: find('rematch'),
      rematchLabel: find('rematch-label'),
    };
    this.#els.rematch.addEventListener('click', () => onRematch());
  }

  show(state, myColor) {
    const outcome = describeOutcome(state.outcome, myColor);
    this.#card.dataset.tone = outcome.tone;
    this.#els.emblem.src = outcome.emblem;
    this.#els.kicker.textContent = outcome.kicker;
    this.#els.title.textContent = outcome.title;
    this.#els.detail.textContent = outcome.detail;
    this.update(state, myColor);

    if (!this.#dialog.open) this.#dialog.showModal();
    if (outcome.tone === 'win') this.#celebrate();
  }

  /** Keeps the rematch button in sync while the dialog is open. */
  update(state, myColor) {
    const { rematch, rematchLabel, note } = this.#els;
    const opponentColor = myColor === 'white' ? 'black' : 'white';
    const me = myColor ? state[myColor] : null;
    const opponent = myColor ? state[opponentColor] : null;

    rematch.hidden = !myColor;
    rematch.disabled = false;

    if (!myColor) {
      note.textContent = '';
    } else if (state.rematchCode) {
      rematchLabel.textContent = 'Starting…';
      rematch.disabled = true;
      note.textContent = 'Rematch accepted!';
    } else if (me?.rematchRequested) {
      rematchLabel.textContent = 'Rematch sent';
      rematch.disabled = true;
      note.textContent = opponent?.connected === false ? 'Your opponent left the game.' : 'Waiting for your opponent…';
    } else if (opponent?.rematchRequested) {
      rematchLabel.textContent = 'Accept rematch';
      note.textContent = 'Your opponent wants a rematch!';
    } else {
      rematchLabel.textContent = 'Rematch';
      note.textContent = opponent?.connected === false ? 'Your opponent left the game.' : '';
    }
  }

  close() {
    if (this.#dialog.open) this.#dialog.close();
  }

  #celebrate() {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    for (let i = 0; i < 28; i++) {
      const piece = document.createElement('span');
      piece.className = 'confetti';
      piece.style.left = `${Math.random() * 100}%`;
      piece.style.background = CONFETTI_COLORS[i % CONFETTI_COLORS.length];
      piece.style.animationDelay = `${Math.random() * 0.35}s`;
      piece.style.rotate = `${Math.random() * 360}deg`;
      this.#card.append(piece);
      setTimeout(() => piece.remove(), 2200);
    }
  }
}
