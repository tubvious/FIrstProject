import { FILES, colorOf, fileIndex, rankIndex } from './position.js';

const DRAG_THRESHOLD_PX = 4;
const PROMOTION_PIECES = ['q', 'n', 'r', 'b'];
const PIECE_NAMES = { q: 'queen', n: 'knight', r: 'rook', b: 'bishop' };

const div = (className) => {
  const el = document.createElement('div');
  el.className = className;
  return el;
};

/**
 * Renders a chess board and handles move input (click-click or drag-and-drop, mouse or touch).
 * The board knows nothing about the rules: it only allows the moves it was given via setMovable()
 * and reports the chosen move through the onMove callback.
 */
export class ChessBoard {
  #root;
  #squaresLayer;
  #piecesLayer;
  #squareEls = new Map();
  #pieceEls = new Map();
  #position = new Map();
  #orientation = 'white';
  #coordinates;
  #onMove;
  #movable = null;
  #selected = null;
  #drag = null;
  #hoverSquare = null;
  #highlights = { lastMove: null, check: null };
  #promotion = null;

  /**
   * @param {HTMLElement} root
   * @param {{ onMove?: (uci: string, info: { dragged: boolean }) => void, coordinates?: boolean }} options
   */
  constructor(root, { onMove = null, coordinates = true } = {}) {
    this.#root = root;
    this.#onMove = onMove;
    this.#coordinates = coordinates;

    root.classList.add('board');
    this.#squaresLayer = div('board__squares');
    this.#piecesLayer = div('board__pieces');
    root.append(this.#squaresLayer, this.#piecesLayer);
    this.#buildSquares();

    if (onMove) {
      root.addEventListener('pointerdown', (event) => this.#onPointerDown(event));
      root.addEventListener('pointermove', (event) => this.#onPointerMove(event));
      root.addEventListener('pointerup', (event) => this.#onPointerUp(event));
      root.addEventListener('pointercancel', () => this.#cancelDrag());
      root.addEventListener('contextmenu', (event) => {
        if (this.#selected || this.#drag) {
          event.preventDefault();
          this.cancelInteraction();
        }
      });
      document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') this.cancelInteraction();
      });
    }
  }

  get orientation() {
    return this.#orientation;
  }

  setOrientation(color) {
    if (color === this.#orientation) return;
    this.#orientation = color;
    this.cancelInteraction();
    this.#buildSquares();
    for (const [square, el] of this.#pieceEls) this.#place(el, square, { instant: true });
  }

  /**
   * Shows a position, animating pieces from where they were.
   * @param {Map<string,string>} position square -> piece code
   * @param {{ animate?: boolean, move?: {from: string, to: string} | null, dropped?: boolean }} options
   *   move: the move that led here, so the right piece slides (and a promoting pawn becomes the new piece);
   *   dropped: the moved piece was dragged onto its square, so it should snap rather than slide.
   */
  setPosition(position, { animate = true, move = null, dropped = false } = {}) {
    if (this.#drag) this.#cancelDrag();

    const leftovers = new Map(this.#pieceEls);
    const next = new Map();
    const unmatched = [];

    // Pieces that did not change stay exactly where they are.
    for (const [square, piece] of position) {
      const el = leftovers.get(square);
      if (el && el.dataset.piece === piece) {
        next.set(square, el);
        leftovers.delete(square);
      } else {
        unmatched.push([square, piece]);
      }
    }

    // Everything else either slid from another square or is new.
    for (const [square, piece] of unmatched) {
      const source = this.#findSource(leftovers, square, piece, move);
      let el;
      if (source) {
        el = leftovers.get(source);
        leftovers.delete(source);
        const instant = !animate || (dropped && move?.to === square);
        if (!instant) {
          el.classList.add('piece--moving');
          setTimeout(() => el.classList.remove('piece--moving'), 260);
        }
        el.dataset.piece = piece;
        this.#place(el, square, { instant });
      } else {
        el = this.#createPiece(piece);
        this.#place(el, square, { instant: true });
        if (animate) {
          el.classList.add('piece--appearing');
          el.addEventListener('animationend', () => el.classList.remove('piece--appearing'), { once: true });
        }
      }
      next.set(square, el);
    }

    // Anything left over was captured.
    for (const el of leftovers.values()) {
      if (animate) {
        el.classList.add('piece--captured');
        setTimeout(() => el.remove(), 280);
      } else {
        el.remove();
      }
    }

    this.#pieceEls = next;
    this.#position = new Map(position);
    if (this.#selected && !this.#position.has(this.#selected)) this.#deselect();
    this.#renderInteractivity();
  }

  /** @param {{ lastMove?: {from: string, to: string} | null, check?: string | null }} highlights */
  setHighlights({ lastMove = null, check = null } = {}) {
    this.#highlights = { lastMove, check };
    this.#renderHighlights();
  }

  /**
   * Enables input for one colour.
   * @param {{ color: 'white'|'black', legalMoves: string[] } | null} movable UCI moves the player may make
   */
  setMovable(movable) {
    if (!movable) {
      this.#movable = null;
      this.cancelInteraction();
    } else {
      const destinations = new Map();
      const promotions = new Set();
      for (const uci of movable.legalMoves) {
        const from = uci.slice(0, 2);
        const to = uci.slice(2, 4);
        if (!destinations.has(from)) destinations.set(from, new Set());
        destinations.get(from).add(to);
        if (uci.length === 5) promotions.add(from + to);
      }
      this.#movable = { color: movable.color, destinations, promotions };
      if (this.#selected && !destinations.has(this.#selected)) this.#deselect();
    }
    this.#renderInteractivity();
  }

  /** Brief visual pulse on a square, used for captures. */
  flash(square) {
    const el = this.#squareEls.get(square);
    if (!el) return;
    el.classList.remove('sq--impact');
    void el.offsetWidth; // restart the animation
    el.classList.add('sq--impact');
    setTimeout(() => el.classList.remove('sq--impact'), 500);
  }

  cancelInteraction() {
    this.#cancelDrag();
    this.#promotion?.finish(null);
    this.#deselect();
  }

  // ---------------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------------

  #buildSquares() {
    this.#squaresLayer.replaceChildren();
    this.#squareEls.clear();
    const white = this.#orientation === 'white';

    for (let y = 0; y < 8; y++) {
      for (let x = 0; x < 8; x++) {
        const file = white ? x : 7 - x;
        const rank = white ? 7 - y : y;
        const square = `${FILES[file]}${rank + 1}`;
        const el = div((file + rank) % 2 === 0 ? 'sq sq--dark' : 'sq');
        el.dataset.square = square;

        if (this.#coordinates && x === 0) el.append(this.#coordinate('coord coord--rank', String(rank + 1)));
        if (this.#coordinates && y === 7) el.append(this.#coordinate('coord coord--file', FILES[file]));

        this.#squaresLayer.append(el);
        this.#squareEls.set(square, el);
      }
    }

    this.#renderHighlights();
    this.#renderInteractivity();
  }

  #coordinate(className, text) {
    const span = document.createElement('span');
    span.className = className;
    span.textContent = text;
    return span;
  }

  #createPiece(piece) {
    const el = div('piece');
    el.dataset.piece = piece;
    this.#piecesLayer.append(el);
    return el;
  }

  #coords(square) {
    const file = fileIndex(square);
    const rank = rankIndex(square);
    return this.#orientation === 'white' ? { x: file, y: 7 - rank } : { x: 7 - file, y: rank };
  }

  #place(el, square, { instant = false } = {}) {
    const { x, y } = this.#coords(square);
    if (instant) el.classList.add('piece--instant');
    el.style.transform = `translate(${x * 100}%, ${y * 100}%)`;
    if (instant) {
      void el.offsetWidth; // commit the position before re-enabling transitions
      el.classList.remove('piece--instant');
    }
  }

  #findSource(candidates, square, piece, move) {
    if (move && move.to === square && candidates.has(move.from)) {
      const moving = candidates.get(move.from).dataset.piece;
      if (colorOf(moving) === colorOf(piece)) return move.from;
    }

    let best = null;
    let bestDistance = Infinity;
    for (const [from, el] of candidates) {
      if (el.dataset.piece !== piece) continue;
      const distance = Math.abs(fileIndex(from) - fileIndex(square)) + Math.abs(rankIndex(from) - rankIndex(square));
      if (distance < bestDistance) {
        best = from;
        bestDistance = distance;
      }
    }
    return best;
  }

  #renderHighlights() {
    for (const el of this.#squareEls.values()) el.classList.remove('sq--last', 'sq--check');
    const { lastMove, check } = this.#highlights;
    if (lastMove) {
      this.#squareEls.get(lastMove.from)?.classList.add('sq--last');
      this.#squareEls.get(lastMove.to)?.classList.add('sq--last');
    }
    if (check) this.#squareEls.get(check)?.classList.add('sq--check');
  }

  #renderInteractivity() {
    this.#root.classList.toggle('board--interactive', Boolean(this.#movable));
    for (const [square, el] of this.#squareEls) {
      el.classList.toggle('sq--movable', Boolean(this.#movable?.destinations.has(square)));
    }
    this.#renderSelection();
  }

  #renderSelection() {
    for (const el of this.#squareEls.values()) el.classList.remove('sq--selected', 'sq--target', 'sq--occupied');
    if (!this.#selected) return;

    this.#squareEls.get(this.#selected)?.classList.add('sq--selected');
    for (const to of this.#movable?.destinations.get(this.#selected) ?? []) {
      const el = this.#squareEls.get(to);
      el?.classList.add('sq--target');
      if (this.#position.has(to)) el?.classList.add('sq--occupied');
    }
  }

  // ---------------------------------------------------------------------------
  // Input
  // ---------------------------------------------------------------------------

  #squareAt(clientX, clientY) {
    const rect = this.#root.getBoundingClientRect();
    const size = rect.width / 8;
    const x = Math.floor((clientX - rect.left) / size);
    const y = Math.floor((clientY - rect.top) / size);
    if (x < 0 || x > 7 || y < 0 || y > 7) return null;
    const white = this.#orientation === 'white';
    const file = white ? x : 7 - x;
    const rank = white ? 7 - y : y;
    return `${FILES[file]}${rank + 1}`;
  }

  #isTarget(from, to) {
    return Boolean(this.#movable?.destinations.get(from)?.has(to));
  }

  #select(square) {
    this.#selected = square;
    this.#renderSelection();
  }

  #deselect() {
    if (!this.#selected) return;
    this.#selected = null;
    this.#renderSelection();
  }

  #onPointerDown(event) {
    if (!this.#movable || this.#promotion || (event.pointerType === 'mouse' && event.button !== 0)) return;
    const square = this.#squareAt(event.clientX, event.clientY);
    if (!square) return;

    if (this.#selected && this.#isTarget(this.#selected, square)) {
      event.preventDefault();
      this.#tryMove(this.#selected, square, false);
      return;
    }

    if (this.#movable.destinations.has(square)) {
      event.preventDefault();
      const wasSelected = this.#selected === square;
      this.#select(square);
      this.#drag = {
        from: square,
        el: this.#pieceEls.get(square),
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        active: false,
        wasSelected,
      };
      this.#root.setPointerCapture?.(event.pointerId);
      return;
    }

    this.#deselect();
  }

  #onPointerMove(event) {
    const drag = this.#drag;
    if (!drag || event.pointerId !== drag.pointerId || !drag.el) return;

    if (!drag.active) {
      if (Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) < DRAG_THRESHOLD_PX) return;
      drag.active = true;
      drag.el.classList.add('piece--dragging');
    }

    const rect = this.#root.getBoundingClientRect();
    const size = rect.width / 8;
    const x = (event.clientX - rect.left) / size - 0.5;
    const y = (event.clientY - rect.top) / size - 0.5;
    drag.el.style.transform = `translate(${x * 100}%, ${y * 100}%)`;

    const square = this.#squareAt(event.clientX, event.clientY);
    this.#setHover(square && this.#isTarget(drag.from, square) ? square : null);
  }

  #onPointerUp(event) {
    const drag = this.#drag;
    if (!drag || event.pointerId !== drag.pointerId) return;
    this.#drag = null;
    this.#setHover(null);
    this.#root.releasePointerCapture?.(event.pointerId);

    if (!drag.active) {
      // A plain click on the already-selected piece toggles the selection off.
      if (drag.wasSelected) this.#deselect();
      return;
    }

    drag.el.classList.remove('piece--dragging');
    const target = this.#squareAt(event.clientX, event.clientY);
    if (target && target !== drag.from && this.#isTarget(drag.from, target)) {
      this.#tryMove(drag.from, target, true);
    } else {
      this.#place(drag.el, drag.from);
    }
  }

  #cancelDrag() {
    const drag = this.#drag;
    if (!drag) return;
    this.#drag = null;
    this.#setHover(null);
    if (drag.el) {
      drag.el.classList.remove('piece--dragging');
      this.#place(drag.el, drag.from);
    }
  }

  #setHover(square) {
    if (square === this.#hoverSquare) return;
    if (this.#hoverSquare) this.#squareEls.get(this.#hoverSquare)?.classList.remove('sq--hover');
    this.#hoverSquare = square;
    if (square) this.#squareEls.get(square)?.classList.add('sq--hover');
  }

  async #tryMove(from, to, dragged) {
    let uci = from + to;

    if (this.#movable?.promotions.has(uci)) {
      const pawn = this.#pieceEls.get(from);
      if (pawn) this.#place(pawn, to, { instant: dragged });
      const choice = await this.#pickPromotion(to);
      if (!choice) {
        if (pawn && this.#pieceEls.get(from) === pawn) this.#place(pawn, from);
        return;
      }
      uci += choice;
    }

    this.#deselect();
    this.#onMove?.(uci, { dragged });
  }

  #pickPromotion(square) {
    return new Promise((resolve) => {
      const color = this.#movable?.color === 'black' ? 'b' : 'w';
      const { x, y } = this.#coords(square);
      const fromTop = y < 4;

      const overlay = div('promotion');
      const choices = div('promotion__choices');
      choices.style.left = `${x * 12.5}%`;
      choices.style[fromTop ? 'top' : 'bottom'] = '0';

      const order = fromTop ? PROMOTION_PIECES : [...PROMOTION_PIECES].reverse();
      for (const letter of order) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'promotion__choice';
        button.setAttribute('aria-label', `Promote to ${PIECE_NAMES[letter]}`);
        const img = document.createElement('img');
        img.src = `/img/pieces/${color}${letter.toUpperCase()}.svg`;
        img.alt = '';
        button.append(img);
        button.addEventListener('click', (event) => {
          event.stopPropagation();
          finish(letter);
        });
        choices.append(button);
      }

      const finish = (choice) => {
        if (!this.#promotion) return;
        this.#promotion = null;
        overlay.remove();
        resolve(choice);
      };

      overlay.addEventListener('pointerdown', (event) => {
        event.stopPropagation();
        if (event.target === overlay) finish(null);
      });
      overlay.append(choices);
      this.#root.append(overlay);
      this.#promotion = { finish };
      choices.querySelector('button')?.focus({ preventScroll: true });
    });
  }
}
