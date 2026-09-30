// Turns game state into the words shown to a particular viewer.

export const colorName = (color) => (color === 'white' ? 'White' : 'Black');

const opposite = (color) => (color === 'white' ? 'black' : 'white');

const moveNumber = (state) => Math.floor(state.moves.length / 2) + 1;

/**
 * Describes a finished game from the viewer's point of view.
 * @returns {{ tone: 'win'|'loss'|'draw', kicker: string, title: string, detail: string, emblem: string }}
 */
export function describeOutcome(outcome, myColor) {
  const { result, reason, winner } = outcome;
  const loser = winner ? opposite(winner) : null;
  const tone = !winner ? 'draw' : winner === myColor ? 'win' : myColor ? 'loss' : 'draw';

  const details = {
    checkmate: () => `${colorName(winner)} wins by checkmate`,
    resignation: () => `${colorName(loser)} resigned`,
    timeout: () => `${colorName(loser)} ran out of time`,
    abandonment: () => (winner ? `${colorName(loser)} left the game` : 'Called a draw after a player left'),
    stalemate: () => 'Stalemate: no legal moves, but no check',
    insufficientMaterial: () => 'Neither side has enough material to checkmate',
    threefoldRepetition: () => 'The same position occurred three times',
    fiftyMoveRule: () => 'Fifty moves without a capture or pawn move',
    agreement: () => 'Both players agreed to a draw',
    timeoutVsInsufficientMaterial: () => 'Time ran out, but the opponent could not checkmate',
    aborted: () => 'The game ended before both players moved',
  };

  const kickers = { checkmate: 'Checkmate', resignation: 'Resignation', timeout: 'Time out', abandonment: 'Abandoned' };
  const kicker = result === 'draw' ? 'Draw' : result === 'aborted' ? 'Aborted' : kickers[reason] ?? 'Game over';

  let title;
  if (result === 'aborted') title = 'Game aborted';
  else if (result === 'draw') title = "It's a draw";
  else if (tone === 'win') title = 'You won!';
  else if (tone === 'loss') title = 'You lost';
  else title = `${colorName(winner)} wins`;

  const emblem = winner ? `/img/pieces/${winner === 'white' ? 'w' : 'b'}K.svg` : '/img/pieces/wN.svg';
  return { tone, kicker, title, detail: details[reason]?.() ?? '', emblem };
}

/**
 * Status banner for the side panel.
 * @returns {{ tone: string, icon: string, title: string, detail: string }}
 */
export function describeStatus({ state, myColor, connectionState, reviewPly }) {
  if (connectionState === 'reconnecting') {
    return { tone: 'warning', icon: 'wifi-off', title: 'Reconnecting…', detail: 'Hang tight, restoring the connection' };
  }
  if (connectionState === 'closed') {
    return { tone: 'danger', icon: 'wifi-off', title: 'Disconnected', detail: 'Lost connection to the server' };
  }
  if (!state) {
    return { tone: 'neutral', icon: 'spinner', title: 'Connecting…', detail: 'Setting up the board' };
  }

  if (reviewPly !== null) {
    return {
      tone: 'warning',
      icon: 'eye',
      title: 'Reviewing',
      detail: `Position after move ${reviewPly} of ${state.moves.length}`,
    };
  }

  if (state.status === 'waitingForOpponent') {
    return myColor
      ? { tone: 'accent', icon: 'spinner', title: 'Waiting for opponent', detail: 'Share the link to start the game' }
      : { tone: 'neutral', icon: 'spinner', title: 'Waiting for players', detail: 'The game has not started yet' };
  }

  if (state.status === 'finished' && state.outcome) {
    const outcome = describeOutcome(state.outcome, myColor);
    const tone = outcome.tone === 'win' ? 'success' : outcome.tone === 'loss' ? 'danger' : 'neutral';
    const iconName = outcome.tone === 'win' ? 'trophy' : state.outcome.result === 'draw' ? 'half' : 'flag';
    return { tone, icon: iconName, title: outcome.title, detail: `${outcome.kicker} · ${outcome.detail}` };
  }

  const inCheck = Boolean(state.checkSquare);
  const clockNote = state.clock && state.moves.length < 2 ? ' · Clock starts after the first moves' : '';

  if (!myColor) {
    return {
      tone: inCheck ? 'danger' : 'neutral',
      icon: inCheck ? 'alert' : 'hourglass',
      title: inCheck ? `Check! ${colorName(state.turn)} to move` : `${colorName(state.turn)} to move`,
      detail: `Move ${moveNumber(state)} · You are spectating`,
    };
  }

  const opponent = state[opposite(myColor)];
  if (opponent && !opponent.connected) {
    return { tone: 'warning', icon: 'wifi-off', title: 'Opponent disconnected', detail: 'Waiting for them to reconnect…' };
  }

  if (state.turn === myColor) {
    return {
      tone: inCheck ? 'danger' : 'accent',
      icon: inCheck ? 'alert' : 'arrow-right',
      title: inCheck ? 'Check! Your turn' : 'Your turn',
      detail: `Move ${moveNumber(state)} · You play ${colorName(myColor)}${clockNote}`,
    };
  }

  return {
    tone: 'neutral',
    icon: 'hourglass',
    title: "Opponent's turn",
    detail: inCheck ? 'You gave check!' : `Move ${moveNumber(state)} · You play ${colorName(myColor)}${clockNote}`,
  };
}
