// Small shared UI helpers: icons, toasts, dialogs and clipboard.

const ICON_SPRITE = '/img/icons.svg';

/** Returns an <svg> element that references an icon in the sprite. */
export function icon(name, className = '') {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('aria-hidden', 'true');
  if (className) svg.setAttribute('class', className);
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `${ICON_SPRITE}#${name}`);
  svg.append(use);
  return svg;
}

/** Creates a button with an optional icon. */
export function button(label, { variant = 'soft', iconName = null, onClick, size = null } = {}) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = ['btn', `btn--${variant}`, size ? `btn--${size}` : ''].filter(Boolean).join(' ');
  if (iconName) el.append(icon(iconName));
  el.append(document.createTextNode(label));
  if (onClick) el.addEventListener('click', onClick);
  return el;
}

/**
 * Shows a transient notification.
 * @param {string} message
 * @param {{ type?: 'info'|'success'|'error'|'warning', duration?: number }} options
 */
export function toast(message, { type = 'info', duration = 3200 } = {}) {
  const container = document.getElementById('toasts');
  if (!container) return;

  const el = document.createElement('div');
  el.className = `toast toast--${type}`;
  const dot = document.createElement('span');
  dot.className = 'toast__dot';
  const text = document.createElement('span');
  text.textContent = message;
  el.append(dot, text);
  container.append(el);

  // Keep the stack short.
  while (container.children.length > 3) container.firstElementChild.remove();

  setTimeout(() => {
    el.classList.add('toast--leaving');
    el.addEventListener('animationend', () => el.remove(), { once: true });
  }, duration);
}

/** Wires elements with [data-close] inside a dialog, and closes it when the backdrop is clicked. */
export function enhanceDialog(dialog) {
  dialog.querySelectorAll('[data-close]').forEach((el) => el.addEventListener('click', () => dialog.close()));
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) dialog.close();
  });
  return dialog;
}

/**
 * Asks for confirmation using the page's #confirm-dialog.
 * @returns {Promise<boolean>}
 */
export function confirmDialog({ title, message, confirmText = 'Confirm', danger = true }) {
  const dialog = document.getElementById('confirm-dialog');
  if (!dialog) return Promise.resolve(window.confirm(`${title}\n\n${message}`));

  dialog.querySelector('[data-role="title"]').textContent = title;
  dialog.querySelector('[data-role="message"]').textContent = message;
  const confirmButton = dialog.querySelector('[data-role="confirm"]');
  confirmButton.textContent = confirmText;
  confirmButton.className = `btn btn--lg ${danger ? 'btn--danger' : 'btn--primary'}`;

  return new Promise((resolve) => {
    dialog.returnValue = '';
    dialog.addEventListener('close', () => resolve(dialog.returnValue === 'confirm'), { once: true });
    dialog.showModal();
  });
}

/** Copies text to the clipboard, with a fallback for non-secure contexts. */
export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const input = document.createElement('textarea');
    input.value = text;
    input.setAttribute('readonly', '');
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.append(input);
    input.select();
    let copied = false;
    try {
      copied = document.execCommand('copy');
    } catch {
      copied = false;
    }
    input.remove();
    return copied;
  }
}
