let host = null;

/** Aviso rápido no rodapé; some sozinho. */
export function toast(message, kind = 'info', ms = 3500) {
  if (!host) {
    host = document.createElement('div');
    host.className = 'toasts';
    host.setAttribute('role', 'status');
    host.setAttribute('aria-live', 'polite');
    document.body.append(host);
  }
  const el = document.createElement('div');
  el.className = `toast${kind === 'error' ? ' toast--error' : ''}`;
  el.textContent = message;
  host.append(el);
  setTimeout(() => el.remove(), ms);
}
