// Display-only helpers for board positions. The client never decides whether a move is legal;
// these functions only mirror a move the server already listed as legal, for instant feedback.

export const FILES = 'abcdefgh';
export const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Parses the placement part of a FEN into a Map of square -> piece code (e.g. "e1" -> "wK"). */
export function parseFen(fen) {
  const board = new Map();
  const ranks = fen.split(' ')[0].split('/');
  ranks.forEach((row, index) => {
    const rank = 8 - index;
    let file = 0;
    for (const char of row) {
      if (/\d/.test(char)) {
        file += Number(char);
      } else {
        const color = char === char.toUpperCase() ? 'w' : 'b';
        board.set(`${FILES[file]}${rank}`, color + char.toUpperCase());
        file++;
      }
    }
  });
  return board;
}

export const sideToMove = (fen) => (fen.split(' ')[1] === 'b' ? 'black' : 'white');

export const colorOf = (piece) => (piece?.[0] === 'w' ? 'white' : piece?.[0] === 'b' ? 'black' : null);

export const fileIndex = (square) => FILES.indexOf(square[0]);

export const rankIndex = (square) => Number(square[1]) - 1;

export function findKing(board, color) {
  const king = `${color === 'white' ? 'w' : 'b'}K`;
  for (const [square, piece] of board) {
    if (piece === king) return square;
  }
  return null;
}

/** Applies a UCI move (already known to be legal) to a board map, handling castling, en passant and promotion. */
export function applyUciMove(board, uci) {
  const next = new Map(board);
  const from = uci.slice(0, 2);
  const to = uci.slice(2, 4);
  const promotion = uci[4];
  const piece = next.get(from);
  if (!piece) return next;

  const type = piece[1];
  const fileDelta = fileIndex(to) - fileIndex(from);

  if (type === 'P' && fileDelta !== 0 && !next.has(to)) {
    // En passant: the captured pawn sits beside the moving pawn.
    next.delete(`${to[0]}${from[1]}`);
  }

  if (type === 'K' && Math.abs(fileDelta) === 2) {
    const rank = from[1];
    const [rookFrom, rookTo] = fileDelta > 0 ? [`h${rank}`, `f${rank}`] : [`a${rank}`, `d${rank}`];
    next.set(rookTo, next.get(rookFrom));
    next.delete(rookFrom);
  }

  next.delete(from);
  next.set(to, promotion ? piece[0] + promotion.toUpperCase() : piece);
  return next;
}
