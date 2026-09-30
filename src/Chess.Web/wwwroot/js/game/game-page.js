import { ChessBoard } from '../board/chess-board.js';
import { START_FEN, applyUciMove, findKing, parseFen, sideToMove } from '../board/position.js';
import { getPlayerName } from '../common/player-name.js';
import { sounds } from '../common/sound.js';
import { seats } from '../common/storage.js';
import { button, confirmDialog, copyText, icon, toast } from '../common/ui.js';
import { ClockTicker } from './clock.js';
import { GameConnection } from './connection.js';
import { MoveList } from './move-list.js';
import { PlayerBar } from './player-bar.js';
import { ResultDialog } from './result-dialog.js';
import { colorName, describeStatus } from './status.js';

const LOW_TIME_MS = 20_000;

const opposite = (color) => (color === 'white' ? 'black' : 'white');
const byId = (id) => document.getElementById(id);

/**
 * Controller for the game screen. The server is the single source of truth: every snapshot it sends
 * is rendered as-is. The only local state is presentation (review mode, pending optimistic move).
 */
class GamePage {
  #root;
  #code;
  #role = null;
  #state = null;
  #receivedAt = 0;
  #pendingMove = null;
  #reviewPly = null;
  #connectionState = 'connecting';
  #resultShown = false;
  #animateNextState = false;
  #promptKey = null;

  #board;
  #bars;
  #moveList;
  #clock;
  #result;
  #connection;

  constructor(root) {
    this.#root = root;
    this.#code = root.dataset.gameCode;

    this.#board = new ChessBoard(byId('board'), { onMove: (uci, info) => this.#submitMove(uci, info) });
    this.#board.setPosition(parseFen(START_FEN), { animate: false });
    this.#bars = { top: new PlayerBar(byId('bar-top')), bottom: new PlayerBar(byId('bar-bottom')) };
    this.#moveList = new MoveList(byId('moves'), { onSelect: (ply) => this.#review(ply) });
    this.#clock = new ClockTicker(() => this.#renderClocks());
    this.#result = new ResultDialog(byId('result-dialog'), { onRematch: () => this.#requestRematch() });
    this.#connection = new GameConnection({
      onState: (state) => this.#applyState(state),
      onNotification: (notification) => this.#notify(notification),
      onReconnecting: () => this.#setConnectionState('reconnecting'),
      onReconnected: () => this.#handleReconnected(),
      onClosed: () => this.#setConnectionState('closed'),
    });

    this.#bindControls();
    this.#renderMeta();
    setInterval(() => this.#renderPrompt(), 1000);
  }

  get #myColor() {
    return this.#role === 'white' || this.#role === 'black' ? this.#role : null;
  }

  get #isLive() {
    return this.#reviewPly === null;
  }

  async start() {
    try {
      await this.#connection.start();
    } catch {
      this.#setConnectionState('closed');
      return;
    }
    this.#setConnectionState('connected');
    await this.#join();
  }

  // ---------------------------------------------------------------------------
  // Server communication
  // ---------------------------------------------------------------------------

  async #join() {
    const response = await this.#connection.invoke('JoinGame', {
      code: this.#code,
      seatToken: seats.tabToken(this.#code),
      knownSeatTokens: seats.knownTokens(this.#code),
      playerName: getPlayerName(),
    });

    if (!response?.success) {
      this.#showError(
        response?.error === 'GameNotFound' ? 'Game not found' : 'Could not join the game',
        response?.message ?? 'Please try again.');
      return;
    }

    this.#role = response.role;
    if (response.seatToken) seats.remember(this.#code, response.seatToken);
    this.#applyState(response.state, { force: true });
  }

  #applyState(state, { force = false } = {}) {
    if (!state || state.code !== this.#code) return;
    const previous = this.#state;
    if (previous && !force && state.version <= previous.version) return;

    this.#state = state;
    this.#receivedAt = performance.now();

    const newMove = previous && state.moves.length === previous.moves.length + 1 ? state.moves.at(-1) : null;
    const justFinished = previous && previous.status !== 'finished' && state.status === 'finished';
    const animate = Boolean(previous) || this.#animateNextState;
    this.#animateNextState = false;
    this.#pendingMove = null;
    if (newMove) this.#reviewPly = null;

    this.#renderBoard({ animate });
    this.#renderPlayers();
    this.#clock.sync(state.clock);
    this.#renderStatus();
    this.#renderPrompt();
    this.#renderMeta();
    this.#renderMoves();
    this.#renderActions();
    this.#renderOverlay();
    this.#updateTitle();

    if (newMove) {
      if (newMove.isCapture) this.#board.flash(newMove.to);
      if (!justFinished) sounds.play(newMove.isCheck ? 'check' : newMove.isCapture ? 'capture' : 'move');
      if (newMove.isCheck && !justFinished) this.#bumpStatus();
    }

    if (state.status === 'finished') {
      if (justFinished) sounds.play('gameEnd');
      if (!this.#resultShown) {
        this.#resultShown = true;
        // Let the final move land before the result card appears.
        setTimeout(() => this.#state?.code === state.code && this.#result.show(this.#state, this.#myColor), justFinished ? 650 : 0);
      } else {
        this.#result.update(state, this.#myColor);
      }
    }

    // Both players agreed to a rematch while we were watching: follow it.
    if (state.rematchCode && previous && !previous.rematchCode) this.#switchGame(state.rematchCode);
  }

  async #submitMove(uci, { dragged }) {
    if (!this.#canMove()) return;

    // Show the move immediately; the server confirms (or rejects) it a moment later.
    const move = { from: uci.slice(0, 2), to: uci.slice(2, 4) };
    this.#pendingMove = uci;
    this.#board.setMovable(null);
    this.#board.setPosition(applyUciMove(parseFen(this.#state.fen), uci), { move, dropped: dragged });
    this.#board.setHighlights({ lastMove: move, check: null });

    const result = await this.#connection.invoke('MakeMove', uci);
    if (!result.success && this.#pendingMove === uci) {
      this.#pendingMove = null;
      toast(result.message ?? 'That move was not accepted.', { type: 'error' });
      this.#renderBoard({ animate: true });
    }
  }

  async #command(method, args = [], successMessage = null) {
    const result = await this.#connection.invoke(method, ...args);
    if (!result.success) {
      toast(result.message ?? 'Something went wrong.', { type: 'error' });
    } else if (successMessage) {
      toast(successMessage);
    }
    return result.success;
  }

  async #resign() {
    const abort = this.#state?.canAbort;
    const confirmed = await confirmDialog(abort
      ? { title: 'Abort this game?', message: 'No moves have been made, so the game ends without a result.', confirmText: 'Abort game' }
      : { title: 'Resign this game?', message: 'Your opponent will be awarded the win.', confirmText: 'Resign' });
    if (confirmed) await this.#command('Resign');
  }

  #requestRematch() {
    const state = this.#state;
    if (state?.rematchCode) {
      this.#switchGame(state.rematchCode);
      return;
    }
    this.#command('RequestRematch');
  }

  async #switchGame(code) {
    const token = seats.tabToken(this.#code);
    if (token) seats.remember(code, token);

    this.#code = code;
    this.#root.dataset.gameCode = code;
    history.replaceState(null, '', `/game/${code}`);
    this.#state = null;
    this.#role = null;
    this.#pendingMove = null;
    this.#reviewPly = null;
    this.#resultShown = false;
    this.#promptKey = null;
    this.#animateNextState = true;
    this.#result.close();

    await this.#join();
    if (this.#state) {
      toast(this.#myColor ? `Rematch! You play ${colorName(this.#myColor)}.` : 'Following the rematch.', { type: 'success' });
    }
  }

  #setConnectionState(connectionState) {
    this.#connectionState = connectionState;
    if (connectionState !== 'connected') this.#board.cancelInteraction();
    if (this.#state) this.#renderBoard({ animate: false });
    this.#renderStatus();
    this.#renderOverlay();
  }

  async #handleReconnected() {
    this.#setConnectionState('connected');
    await this.#join();
    toast('Reconnected', { type: 'success' });
  }

  async #retry() {
    if (!this.#connection.isDisconnected) return;
    this.#setConnectionState('connecting');
    await this.start();
  }

  #notify({ type, color }) {
    const myColor = this.#myColor;
    if (color === myColor) return;
    const name = this.#state?.[color]?.name ?? colorName(color);
    const opponentLabel = myColor ? 'Your opponent' : colorName(color);

    switch (type) {
      case 'playerJoined':
        toast(`${name} joined. Game on!`, { type: 'success' });
        sounds.play('notify');
        break;
      case 'playerDisconnected':
        toast(`${opponentLabel} disconnected`, { type: 'warning' });
        break;
      case 'playerReconnected':
        toast(`${opponentLabel} is back`, { type: 'success' });
        break;
      case 'drawOffered':
        if (myColor) {
          toast('Your opponent offered a draw');
          sounds.play('notify');
        }
        break;
      case 'drawDeclined':
        if (myColor) toast('Your draw offer was declined', { type: 'warning' });
        break;
      case 'rematchRequested':
        if (myColor) {
          toast('Your opponent wants a rematch');
          sounds.play('notify');
        }
        break;
      case 'rematchDeclined':
        if (myColor) toast('Your opponent declined the rematch', { type: 'warning' });
        break;
    }
  }

  // ---------------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------------

  #canMove() {
    const state = this.#state;
    return Boolean(
      state && this.#isLive && this.#myColor && !this.#pendingMove &&
      this.#connectionState === 'connected' &&
      state.status === 'inProgress' && state.turn === this.#myColor);
  }

  /** [slot, colour] pairs: the viewer's colour is always at the bottom. */
  #slots() {
    const bottom = this.#board.orientation;
    return [['top', opposite(bottom)], ['bottom', bottom]];
  }

  #renderBoard({ animate }) {
    const state = this.#state;
    if (!state) return;
    this.#board.setOrientation(this.#myColor ?? 'white');

    const ply = this.#reviewPly ?? state.moves.length;
    const move = ply > 0 ? state.moves[ply - 1] : null;
    const fen = this.#isLive ? state.fen : move?.fen ?? START_FEN;
    const position = parseFen(fen);
    const lastMove = move ? { from: move.from, to: move.to } : null;
    const check = this.#isLive ? state.checkSquare : move?.isCheck ? findKing(position, sideToMove(fen)) : null;

    this.#board.setPosition(position, { animate, move: lastMove });
    this.#board.setHighlights({ lastMove, check });
    this.#board.setMovable(this.#canMove() ? { color: this.#myColor, legalMoves: state.legalMoves } : null);
  }

  #renderPlayers() {
    const state = this.#state;
    for (const [slot, color] of this.#slots()) {
      this.#bars[slot].render({
        color,
        player: state[color],
        isYou: color === this.#myColor,
        isTurn: state.status === 'inProgress' && state.turn === color,
        captured: color === 'white' ? state.captured.byWhite : state.captured.byBlack,
        advantage: color === 'white' ? state.materialBalance : -state.materialBalance,
        hasClock: Boolean(state.clock),
        waiting: state.status === 'waitingForOpponent',
      });
    }
    this.#renderClocks();
  }

  #renderClocks() {
    const state = this.#state;
    if (!state?.clock) return;
    for (const [slot, color] of this.#slots()) {
      const ms = this.#clock.remaining(color);
      this.#bars[slot].setClock(ms, {
        active: state.status === 'inProgress' && state.clock.running === color,
        low: state.status === 'inProgress' && ms < LOW_TIME_MS,
      });
    }
  }

  #renderStatus() {
    const status = describeStatus({
      state: this.#state,
      myColor: this.#myColor,
      connectionState: this.#connectionState,
      reviewPly: this.#reviewPly,
    });
    const el = byId('status');
    el.dataset.tone = status.tone;
    el.querySelector('[data-role="title"]').textContent = status.title;
    el.querySelector('[data-role="detail"]').textContent = status.detail;

    const iconSlot = el.querySelector('[data-role="icon"]');
    if (iconSlot.dataset.icon !== status.icon) {
      iconSlot.dataset.icon = status.icon;
      if (status.icon === 'spinner') {
        const spinner = document.createElement('div');
        spinner.className = 'spinner';
        iconSlot.replaceChildren(spinner);
      } else {
        iconSlot.replaceChildren(icon(status.icon));
      }
    }
  }

  #bumpStatus() {
    const el = byId('status');
    el.classList.remove('status--bump');
    void el.offsetWidth;
    el.classList.add('status--bump');
  }

  /** The contextual prompt: draw offers, rematch requests and an absent opponent. */
  #renderPrompt() {
    const state = this.#state;
    const myColor = this.#myColor;
    const prompt = byId('prompt');
    let model = null;

    if (state && myColor) {
      const opponent = state[opposite(myColor)];
      if (state.status === 'inProgress' && state.drawOfferedBy === opposite(myColor)) {
        model = {
          key: 'draw',
          text: 'Your opponent offered a draw.',
          actions: [
            ['Accept', 'primary', 'check', () => this.#command('RespondToDrawOffer', [true])],
            ['Decline', 'soft', 'x', () => this.#command('RespondToDrawOffer', [false])],
          ],
        };
      } else if (state.status === 'inProgress' && opponent && !opponent.connected) {
        const waitedMs = (opponent.disconnectedForMs ?? 0) + (performance.now() - this.#receivedAt);
        const remaining = Math.ceil(Math.max(0, state.reconnectGraceMs - waitedMs) / 1000);
        model = remaining > 0
          ? { key: 'absent-wait', text: `Opponent disconnected. Waiting for them to reconnect… ${remaining}s`, actions: [] }
          : {
            key: 'absent-claim',
            text: "Your opponent hasn't come back.",
            actions: [
              ['Claim victory', 'primary', 'trophy', () => this.#command('ClaimAbandonedGame', [false])],
              ['Call it a draw', 'soft', 'half', () => this.#command('ClaimAbandonedGame', [true])],
            ],
          };
      } else if (state.status === 'finished' && !state.rematchCode && opponent?.rematchRequested && !state[myColor]?.rematchRequested) {
        model = {
          key: 'rematch',
          text: 'Your opponent wants a rematch.',
          actions: [
            ['Accept', 'primary', 'refresh', () => this.#requestRematch()],
            ['Decline', 'soft', 'x', () => this.#command('DeclineRematch')],
          ],
        };
      }
    }

    if (!model) {
      prompt.hidden = true;
      this.#promptKey = null;
      return;
    }

    prompt.querySelector('[data-role="text"]').textContent = model.text;
    if (model.key !== this.#promptKey) {
      this.#promptKey = model.key;
      prompt.querySelector('[data-role="actions"]').replaceChildren(
        ...model.actions.map(([label, variant, iconName, onClick]) => button(label, { variant, iconName, onClick, size: 'sm' })));
      prompt.hidden = false;
    }
  }

  #renderMeta() {
    const state = this.#state;
    const link = `${window.location.origin}/game/${this.#code}`;
    byId('meta-code').textContent = this.#code;
    document.querySelectorAll('#overlay-invite [data-role="code"]').forEach((el) => { el.textContent = this.#code; });
    document.querySelectorAll('#overlay-invite [data-role="link"]').forEach((el) => { el.value = link; });

    if (!state) return;
    const clock = state.clock;
    byId('chip-time').querySelector('[data-role="text"]').textContent = clock
      ? `${clock.initialMs / 60000}+${clock.incrementMs / 1000}`
      : 'No clock';
    byId('chip-role').querySelector('[data-role="text"]').textContent = this.#myColor
      ? `You play ${colorName(this.#myColor)}`
      : 'Spectating';
    const spectators = byId('chip-spectators');
    spectators.hidden = state.spectatorCount === 0;
    spectators.querySelector('[data-role="text"]').textContent = String(state.spectatorCount);
  }

  #renderMoves() {
    const moves = this.#state?.moves ?? [];
    this.#moveList.render(moves, this.#reviewPly);

    const ply = this.#reviewPly ?? moves.length;
    const nav = byId('review-bar');
    nav.querySelector('[data-nav="first"]').disabled = ply === 0;
    nav.querySelector('[data-nav="prev"]').disabled = ply === 0;
    nav.querySelector('[data-nav="next"]').disabled = this.#isLive;
    nav.querySelector('[data-nav="last"]').disabled = this.#isLive;
  }

  #renderActions() {
    const state = this.#state;
    const myColor = this.#myColor;
    const actions = byId('actions');
    const draw = byId('btn-draw');
    const resign = byId('btn-resign');
    const rematch = byId('btn-rematch');
    const newGame = byId('btn-new');
    const label = (el, text) => { el.querySelector('[data-role="label"]').textContent = text; };

    const playing = Boolean(myColor) && state?.status === 'inProgress';
    const finished = state?.status === 'finished';
    actions.hidden = !(playing || finished);

    draw.hidden = !playing;
    resign.hidden = !playing;
    if (playing) {
      const offered = state.drawOfferedBy === myColor;
      draw.disabled = offered;
      label(draw, offered ? 'Draw offered' : 'Offer draw');
      label(resign, state.canAbort ? 'Abort' : 'Resign');
    }

    rematch.hidden = !(finished && myColor);
    newGame.hidden = !finished;
    if (finished && myColor) {
      const requested = state[myColor]?.rematchRequested;
      rematch.disabled = Boolean(requested) && !state.rematchCode;
      label(rematch, state.rematchCode
        ? 'Go to rematch'
        : requested ? 'Rematch sent' : state[opposite(myColor)]?.rematchRequested ? 'Accept rematch' : 'Rematch');
    }
  }

  #renderOverlay() {
    const state = this.#state;
    const show = (id) => {
      for (const overlayId of ['overlay-connecting', 'overlay-invite', 'overlay-error']) byId(overlayId).hidden = overlayId !== id;
      byId('board-overlay').hidden = id === null;
    };

    if (this.#connectionState === 'closed') {
      this.#setErrorText('Connection lost', 'We could not reach the server. Check your connection and try again.');
      show('overlay-error');
    } else if (!state) {
      show(this.#connectionState === 'error' ? 'overlay-error' : 'overlay-connecting');
    } else if (state.status === 'waitingForOpponent' && this.#myColor) {
      show('overlay-invite');
    } else {
      show(null);
    }
  }

  #showError(title, message) {
    this.#connectionState = 'error';
    this.#setErrorText(title, message);
    this.#renderOverlay();
    this.#renderStatus();
  }

  #setErrorText(title, message) {
    const overlay = byId('overlay-error');
    overlay.querySelector('[data-role="title"]').textContent = title;
    overlay.querySelector('[data-role="message"]').textContent = message;
  }

  #updateTitle() {
    const state = this.#state;
    const myTurn = state?.status === 'inProgress' && state.turn === this.#myColor;
    document.title = myTurn ? '● Your turn · Chess' : `Game ${this.#code} · Chess`;
  }

  // ---------------------------------------------------------------------------
  // Review (browsing earlier positions)
  // ---------------------------------------------------------------------------

  #review(ply) {
    const total = this.#state?.moves.length ?? 0;
    const target = ply === null || ply >= total ? null : Math.max(0, ply);
    if (target === this.#reviewPly) return;

    this.#reviewPly = target;
    this.#board.cancelInteraction();
    this.#renderBoard({ animate: true });
    this.#renderMoves();
    this.#renderStatus();
  }

  #bindControls() {
    document.querySelectorAll('[data-action="copy-link"]').forEach((el) =>
      el.addEventListener('click', async () => {
        const copied = await copyText(`${window.location.origin}/game/${this.#code}`);
        toast(copied ? 'Game link copied. Send it to a friend!' : 'Could not copy the link', { type: copied ? 'success' : 'error' });
      }));

    byId('overlay-error').querySelector('[data-action="retry"]').addEventListener('click', () => {
      if (this.#connection.isDisconnected) this.#retry();
      else window.location.reload();
    });

    byId('btn-draw').addEventListener('click', () => this.#command('OfferDraw', [], 'Draw offer sent'));
    byId('btn-resign').addEventListener('click', () => this.#resign());
    byId('btn-rematch').addEventListener('click', () => this.#requestRematch());

    const nav = byId('review-bar');
    const current = () => this.#reviewPly ?? this.#state?.moves.length ?? 0;
    const go = {
      first: () => this.#review(0),
      prev: () => this.#review(current() - 1),
      next: () => this.#review(current() + 1),
      last: () => this.#review(null),
    };
    nav.addEventListener('click', (event) => {
      const target = event.target.closest('[data-nav]');
      if (target) go[target.dataset.nav]();
    });

    document.addEventListener('keydown', (event) => {
      if (event.target.closest('input, textarea, dialog') || event.altKey || event.ctrlKey || event.metaKey) return;
      const keys = { ArrowLeft: go.prev, ArrowRight: go.next, Home: go.first, End: go.last, ArrowUp: go.first, ArrowDown: go.last };
      if (keys[event.key]) {
        event.preventDefault();
        keys[event.key]();
      }
    });

    document.addEventListener('visibilitychange', () => {
      if (!document.hidden && this.#connection.isDisconnected) this.#retry();
    });
  }
}

const root = byId('game');
if (root) new GamePage(root).start();
