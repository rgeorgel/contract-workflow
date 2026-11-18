import { h } from './dom.js';

/**
 * Abre um <dialog> modal. `body` é um nó; `actions` define os botões do rodapé.
 * Retorna { dialog, close }. Esc e o botão Cancelar fecham.
 */
export function openModal({ title, description, body, actions = [], onClose }) {
  const dialog = h('dialog', { class: 'modal', 'aria-labelledby': 'modal-title' },
    h('div', { class: 'modal__header' },
      h('h2', { id: 'modal-title' }, title),
      description ? h('p', {}, description) : null),
    body ? h('div', { class: 'modal__body' }, body) : null,
    actions.length ? h('div', { class: 'modal__footer' }, actions) : null,
  );
  const close = () => dialog.close();
  dialog.addEventListener('close', () => { dialog.remove(); onClose?.(); });
  document.body.append(dialog);
  dialog.showModal();
  return { dialog, close };
}

/** Confirmação simples. Resolve true/false. */
export function confirmModal({ title, description, confirmLabel = 'Confirmar', danger = false }) {
  return new Promise((resolve) => {
    let answer = false;
    const { close } = openModal({
      title,
      description,
      actions: [
        h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Cancelar'),
        h('button', {
          type: 'button',
          class: `btn ${danger ? 'btn--danger' : 'btn--primary'}`,
          onclick: () => { answer = true; close(); },
        }, confirmLabel),
      ],
      onClose: () => resolve(answer),
    });
  });
}
