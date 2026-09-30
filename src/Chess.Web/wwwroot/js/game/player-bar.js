import { formatClock } from './clock.js';

const PIECE_LETTERS = { pawn: 'P', knight: 'N', bishop: 'B', rook: 'R', queen: 'Q', king: 'K' };

/** The strip above/below the board showing one player. */
export class PlayerBar {
  #root;
  #els;
  #capturedKey = null;

  constructor(root) {
    this.#root = root;
    const find = (role) => root.querySelector(`[data-role="${role}"]`);
    this.#els = {
      avatar: find('avatar'),
      avatarImg: find('avatar-img'),
      name: find('name'),
      you: find('you'),
      presence: find('presence'),
      captured: find('captured'),
      clock: find('clock'),
    };
  }

  /**
   * @param {{ color: 'white'|'black', player: {name: string, connected: boolean} | null, isYou: boolean,
   *           isTurn: boolean, captured: string[], advantage: number, hasClock: boolean, waiting: boolean }} model
   */
  render({ color, player, isYou, isTurn, captured, advantage, hasClock, waiting }) {
    const els = this.#els;
    const letter = color === 'white' ? 'w' : 'b';

    els.avatar.dataset.color = color;
    els.avatarImg.src = `/img/pieces/${letter}K.svg`;

    els.name.textContent = player?.name ?? (waiting ? 'Waiting for opponent…' : 'Empty seat');
    els.name.classList.toggle('player-bar__name--empty', !player);
    els.name.title = player ? `${player.name} (${color})` : '';
    els.you.hidden = !isYou;

    els.presence.hidden = !player;
    if (player) {
      els.presence.dataset.state = player.connected ? 'online' : 'offline';
      els.presence.title = player.connected ? 'Online' : 'Disconnected';
    }

    this.#root.dataset.turn = String(isTurn);
    els.clock.hidden = !hasClock;
    this.#renderCaptured(color === 'white' ? 'b' : 'w', captured, advantage);
  }

  setClock(ms, { active, low }) {
    const text = formatClock(ms);
    if (this.#els.clock.textContent !== text) this.#els.clock.textContent = text;
    this.#els.clock.dataset.active = String(active);
    this.#els.clock.dataset.low = String(low);
  }

  /** Shows the opponent pieces this player captured, grouped by type, plus their material lead. */
  #renderCaptured(capturedColorLetter, captured, advantage) {
    const key = `${captured.join(',')}|${advantage}`;
    if (key === this.#capturedKey) return;
    this.#capturedKey = key;

    const container = this.#els.captured;
    container.replaceChildren();

    let group = null;
    let groupType = null;
    for (const type of captured) {
      if (type !== groupType) {
        group = document.createElement('span');
        group.className = 'captured-group';
        container.append(group);
        groupType = type;
      }
      const img = document.createElement('img');
      img.src = `/img/pieces/${capturedColorLetter}${PIECE_LETTERS[type]}.svg`;
      img.alt = type;
      group.append(img);
    }

    if (advantage > 0) {
      const material = document.createElement('span');
      material.className = 'material';
      material.textContent = `+${advantage}`;
      container.append(material);
    }
  }
}
