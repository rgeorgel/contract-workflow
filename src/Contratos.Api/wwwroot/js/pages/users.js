import { api, ApiError } from '../api.js';
import { requireUser, ROLES, roleLabel } from '../auth.js';
import { clear, h } from '../ui/dom.js';
import { applyErrors, clearErrors, isEmail, setBusy, setFieldError, showAlert } from '../ui/form.js';
import { confirmModal, openModal } from '../ui/modal.js';
import { toast } from '../ui/toast.js';

await requireUser({ roles: ['Admin'] });

const listEl = document.querySelector('[data-list]');
const STATUS_BADGE = { Ativo: 'badge--success', Convidado: 'badge--warning', Desativado: 'badge--neutral' };

function roleSelect(user) {
  const select = h('select', { class: 'select', 'aria-label': `Perfil de ${user.name || user.email}` },
    Object.entries(ROLES).map(([value, r]) => h('option', { value, selected: value === user.role }, r.label)));
  select.addEventListener('change', async () => {
    select.disabled = true;
    try {
      await api(`/users/${user.id}`, { method: 'PUT', body: { role: select.value } });
      toast(`Perfil alterado para ${roleLabel(select.value)}.`);
      user.role = select.value;
    } catch (err) {
      select.value = user.role;
      toast(err.message, 'error');
    } finally {
      select.disabled = false;
    }
  });
  return select;
}

function actionsFor(user) {
  if (user.isCurrentUser) return h('span', { class: 'muted' }, 'Você');
  const btn = (label, onclick, danger = false) =>
    h('button', { type: 'button', class: `btn btn--link${danger ? ' is-danger' : ''}`, onclick }, label);

  if (user.status === 'Convidado') {
    return h('span', { class: 'row-actions' },
      btn('Reenviar convite', async () => {
        try { await api(`/users/${user.id}/resend-invite`, { method: 'POST' }); toast('Convite reenviado.'); }
        catch (err) { toast(err.message, 'error'); }
      }),
      btn('Cancelar convite', async () => {
        const ok = await confirmModal({ title: 'Cancelar convite?', description: `${user.email} não poderá mais usar o link recebido.`, confirmLabel: 'Cancelar convite', danger: true });
        if (!ok) return;
        try { await api(`/users/${user.id}/invite`, { method: 'DELETE' }); toast('Convite cancelado.'); load(); }
        catch (err) { toast(err.message, 'error'); }
      }, true));
  }

  const active = user.status === 'Ativo';
  return btn(active ? 'Desativar' : 'Reativar', async () => {
    if (active) {
      const ok = await confirmModal({
        title: `Desativar ${user.name}?`,
        description: 'A pessoa perde o acesso na hora. Os contratos e o histórico dela continuam.',
        confirmLabel: 'Desativar', danger: true,
      });
      if (!ok) return;
    }
    try {
      await api(`/users/${user.id}`, { method: 'PUT', body: { active: !active } });
      toast(active ? 'Acesso desativado.' : 'Acesso reativado.');
      load();
    } catch (err) { toast(err.message, 'error'); }
  }, active);
}

function render(users) {
  const rows = users.map((u) => h('tr', {},
    h('td', { 'data-label': 'Pessoa' },
      h('span', { class: 'cell-main' }, u.name || '—'),
      h('span', { class: 'cell-sub' }, u.email)),
    h('td', { 'data-label': 'Perfil' }, u.isCurrentUser ? roleLabel(u.role) : roleSelect(u)),
    h('td', { 'data-label': 'Status' }, h('span', { class: `badge ${STATUS_BADGE[u.status] ?? 'badge--neutral'}` }, u.status)),
    h('td', { class: 'cell-actions' }, actionsFor(u))));

  clear(listEl).append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Pessoa'), h('th', {}, 'Perfil'), h('th', {}, 'Status'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Ações')))),
    h('tbody', {}, rows)));
}

async function load() {
  try {
    render(await api('/users'));
  } catch (err) {
    clear(listEl).append(h('div', { class: 'panel__body' }, h('div', { class: 'alert alert--error' }, err.message)));
  }
}

// ---------- Convite ----------
function openInvite() {
  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  const form = h('form', { class: 'form', novalidate: true, id: 'invite-form' },
    alertEl,
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'invite-email' }, 'E-mail'),
      h('input', { class: 'input', id: 'invite-email', name: 'email', type: 'email', autocomplete: 'off', required: true }),
      h('p', { class: 'field__error', 'data-error-for': 'email' })),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'invite-name' }, 'Nome ', h('span', { class: 'muted' }, '(opcional)')),
      h('input', { class: 'input', id: 'invite-name', name: 'name', type: 'text', maxlength: 100 }),
      h('p', { class: 'field__error', 'data-error-for': 'name' })),
    h('fieldset', { class: 'field fieldset' },
      h('legend', { class: 'field__label' }, 'Perfil'),
      h('div', { class: 'radio-cards' },
        Object.entries(ROLES).map(([value, r]) => h('label', { class: 'radio-card' },
          h('input', { type: 'radio', name: 'role', value, checked: value === 'Editor' }),
          h('span', {}, h('strong', {}, r.label), r.description)))),
      h('p', { class: 'field__error', 'data-error-for': 'role' })));

  const submit = h('button', { type: 'submit', class: 'btn btn--primary', form: 'invite-form' }, 'Enviar convite');
  const { close } = openModal({
    title: 'Convidar pessoa',
    description: 'Ela recebe um e-mail para criar a senha e entrar na equipe.',
    body: form,
    actions: [h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Cancelar'), submit],
  });
  form.elements.email.focus();

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    alertEl.hidden = true;
    const email = form.elements.email.value.trim();
    if (!isEmail(email)) { setFieldError(form, 'email', 'E-mail inválido.'); form.elements.email.focus(); return; }

    setBusy(submit, true);
    try {
      await api('/users/invite', { method: 'POST', body: { email, name: form.elements.name.value.trim(), role: form.elements.role.value } });
      close();
      toast(`Convite enviado para ${email}.`);
      load();
    } catch (err) {
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    } finally {
      setBusy(submit, false);
    }
  });
}

document.querySelector('[data-invite]').addEventListener('click', openInvite);
load();
