import { prefs } from './storage.js';

const ADJECTIVES = [
  'Swift', 'Brave', 'Clever', 'Quiet', 'Bold', 'Lucky', 'Sly', 'Noble', 'Mighty', 'Calm',
  'Witty', 'Daring', 'Gentle', 'Sharp', 'Steady', 'Sneaky', 'Royal', 'Cosmic', 'Silent', 'Fierce',
];
const NOUNS = ['Knight', 'Bishop', 'Rook', 'Pawn', 'Queen', 'King', 'Gambit', 'Castle', 'Tactician', 'Fork'];

export const MAX_NAME_LENGTH = 24;

const pick = (list) => list[Math.floor(Math.random() * list.length)];

export function randomName() {
  return `${pick(ADJECTIVES)} ${pick(NOUNS)}`;
}

/** The name shown to opponents. A random one is generated and remembered on first use. */
export function getPlayerName() {
  let name = prefs.get('playerName', '');
  if (typeof name !== 'string' || !name.trim()) {
    name = randomName();
    prefs.set('playerName', name);
  }
  return name;
}

export function setPlayerName(name) {
  const cleaned = String(name ?? '').replace(/\s+/g, ' ').trim().slice(0, MAX_NAME_LENGTH);
  prefs.set('playerName', cleaned);
  return cleaned;
}
