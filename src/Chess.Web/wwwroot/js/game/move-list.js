/** Move history in the familiar "1. e4 e5" layout. Clicking a move shows that position. */
export class MoveList {
  #root;
  #onSelect;
  #renderedCount = 0;

  constructor(root, { onSelect }) {
    this.#root = root;
    this.#onSelect = onSelect;
    root.addEventListener('click', (event) => {
      const button = event.target.closest('[data-ply]');
      if (button) this.#onSelect(Number(button.dataset.ply));
    });
  }

  /**
   * @param {{ san: string, ply: number }[]} moves
   * @param {number|null} activePly the ply being reviewed, or null when following the live game
   */
  render(moves, activePly) {
    if (moves.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'moves__empty';
      empty.textContent = 'No moves yet';
      this.#root.replaceChildren(empty);
      this.#renderedCount = 0;
      return;
    }

    const isNewMove = moves.length === this.#renderedCount + 1;
    const current = activePly ?? moves.length;
    const list = document.createElement('div');
    list.className = 'moves__list';
    list.setAttribute('role', 'list');

    for (let i = 0; i < moves.length; i += 2) {
      const number = document.createElement('span');
      number.className = 'moves__num';
      number.textContent = `${i / 2 + 1}.`;
      list.append(number, this.#moveButton(moves[i], current), moves[i + 1] ? this.#moveButton(moves[i + 1], current) : document.createElement('span'));
    }

    if (isNewMove) list.querySelector(`[data-ply="${moves.length}"]`)?.classList.add('moves__san--new');
    this.#root.replaceChildren(list);
    this.#renderedCount = moves.length;

    if (activePly === null) {
      this.#root.scrollTop = this.#root.scrollHeight;
    } else {
      list.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest' });
    }
  }

  #moveButton(move, currentPly) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'moves__san';
    button.dataset.ply = String(move.ply);
    button.textContent = move.san;
    button.setAttribute('role', 'listitem');
    if (move.ply === currentPly) button.setAttribute('aria-current', 'true');
    return button;
  }
}
