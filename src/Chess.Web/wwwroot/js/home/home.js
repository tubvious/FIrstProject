import { ChessBoard } from '../board/chess-board.js';
import { START_FEN, applyUciMove, findKing, parseFen } from '../board/position.js';
import { getPlayerName, randomName, setPlayerName } from '../common/player-name.js';
import { seats } from '../common/storage.js';
import { enhanceDialog } from '../common/ui.js';

// Same alphabet as the server: no 0/O or 1/I/L.
const CODE_PATTERN = /^[A-HJKMNP-Z2-9]{8}$/;

// Morphy's "Opera Game" (Paris, 1858), replayed on the landing page.
const PREVIEW_GAME = (
  'e2e4 e7e5 g1f3 d7d6 d2d4 c8g4 d4e5 g4f3 d1f3 d6e5 f1c4 g8f6 f3b3 d8e7 b1c3 c7c6 c1g5 b7b5 ' +
  'c3b5 c6b5 c4b5 b8d7 e1c1 a8d8 d1d7 d8d7 h1d1 e7e6 b5d7 f6d7 b3b8 d7b8 d1d8'
).split(' ');
// Plies (1-based) after which Black is in check; the last one is mate.
const PREVIEW_CHECKS = new Set([21, 29, 31, 33]);

function initPlayerName() {
  const input = document.getElementById('player-name');
  input.value = getPlayerName();
  input.addEventListener('input', () => setPlayerName(input.value));
  input.addEventListener('blur', () => {
    input.value = setPlayerName(input.value) || getPlayerName();
  });
  document.getElementById('random-name').addEventListener('click', () => {
    input.value = setPlayerName(randomName());
  });
}

function initCreateDialog() {
  const dialog = enhanceDialog(document.getElementById('create-dialog'));
  const form = document.getElementById('create-form');
  const error = document.getElementById('create-error');
  const submit = document.getElementById('create-submit');

  // One dialog serves both "Create Game" (a friend) and "Play vs Computer".
  const open = (opponent) => {
    form.querySelector('#create-opponent').value = opponent;
    form.querySelector('#bot-levels').hidden = opponent !== 'computer';
    for (const id of ['create-title', 'create-subtitle']) {
      const el = document.getElementById(id);
      el.textContent = el.dataset[opponent];
    }
    submit.textContent = opponent === 'computer' ? 'Start game' : 'Create game';
    error.textContent = '';
    dialog.showModal();
  };

  document.getElementById('create-open').addEventListener('click', () => open('friend'));
  document.getElementById('bot-open').addEventListener('click', () => open('computer'));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const timeControl = form.querySelector('input[name="timeControl"]:checked');
    const color = form.querySelector('input[name="color"]:checked')?.value ?? 'random';
    const minutes = timeControl?.dataset.minutes ? Number(timeControl.dataset.minutes) : null;
    const incrementSeconds = timeControl?.dataset.increment ? Number(timeControl.dataset.increment) : 0;
    const opponent = form.querySelector('#create-opponent').value;
    const botLevel = opponent === 'computer' ? Number(form.querySelector('input[name="botLevel"]:checked')?.value ?? 3) : null;
    const submitLabel = submit.textContent;

    submit.disabled = true;
    submit.textContent = 'Creating…';
    error.textContent = '';
    try {
      const response = await fetch('/api/games', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ playerName: getPlayerName(), minutes, incrementSeconds, color, opponent, botLevel }),
      });
      if (response.status === 429) throw new Error('You are creating games too quickly. Please wait a moment.');
      if (!response.ok) throw new Error('Could not create the game. Please try again.');

      const game = await response.json();
      seats.remember(game.code, game.seatToken);
      window.location.assign(game.url);
    } catch (err) {
      error.textContent = err instanceof TypeError ? 'Network error. Is the server running?' : err.message;
      submit.disabled = false;
      submit.textContent = submitLabel;
    }
  });
}

/** Accepts a raw code, a code with spaces/dashes, or a full game link. */
function extractCode(text) {
  const trimmed = text.trim();
  const fromLink = trimmed.match(/\/game\/([A-Za-z0-9]{8})/);
  const raw = fromLink ? fromLink[1] : trimmed.replace(/[\s-]/g, '');
  return raw.toUpperCase();
}

function initJoinDialog() {
  const dialog = enhanceDialog(document.getElementById('join-dialog'));
  const form = document.getElementById('join-form');
  const input = document.getElementById('join-code');
  const error = document.getElementById('join-error');
  const submit = document.getElementById('join-submit');

  document.getElementById('join-open').addEventListener('click', () => {
    error.textContent = '';
    dialog.showModal();
    input.focus();
  });

  input.addEventListener('input', () => {
    error.textContent = '';
  });

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const code = extractCode(input.value);
    if (!CODE_PATTERN.test(code)) {
      error.textContent = 'Game codes are 8 characters, like K7QD2MXA.';
      return;
    }

    submit.disabled = true;
    try {
      const response = await fetch(`/api/games/${code}`);
      if (response.status === 404) {
        error.textContent = "We couldn't find a game with that code.";
        return;
      }
      if (!response.ok) throw new Error();
      window.location.assign(`/game/${code}`);
    } catch {
      error.textContent = 'Network error. Please try again.';
    } finally {
      submit.disabled = false;
    }
  });
}

function initHowTo() {
  const dialog = enhanceDialog(document.getElementById('howto-dialog'));
  document.getElementById('howto-open').addEventListener('click', () => dialog.showModal());
}

/** Replays a famous game on the decorative board. */
function initPreviewBoard() {
  const root = document.getElementById('preview-board');
  if (!root) return;

  const board = new ChessBoard(root, { coordinates: false });
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  let position = parseFen(START_FEN);
  let ply = 0;
  board.setPosition(position, { animate: false });

  if (reducedMotion) return;

  const step = () => {
    if (document.hidden) return;
    if (ply >= PREVIEW_GAME.length) {
      position = parseFen(START_FEN);
      ply = 0;
      board.setHighlights({});
      board.setPosition(position, { animate: false });
      return;
    }

    const uci = PREVIEW_GAME[ply++];
    position = applyUciMove(position, uci);
    const move = { from: uci.slice(0, 2), to: uci.slice(2, 4) };
    board.setPosition(position, { move });
    board.setHighlights({ lastMove: move, check: PREVIEW_CHECKS.has(ply) ? findKing(position, 'black') : null });
  };

  setInterval(step, 1400);
}

initPlayerName();
initCreateDialog();
initJoinDialog();
initHowTo();
initPreviewBoard();
