import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { can, EMBED, href, notify } from '../embed.js';
import { clear, h, icon, ICONS } from '../ui/dom.js';
import { applyErrors, clearErrors, setBusy, setFieldError, showAlert } from '../ui/form.js';
import { openModal } from '../ui/modal.js';
import { formatRelative } from '../utils/format.js';

const me = await requireUser();
const canEdit = can(me, 'edit');
const listEl = document.querySelector('[data-list]');
const searchEl = document.querySelector('[data-search]');
let filter = 'active';
let items = [];

function statusBadge(t) {
  if (t.archivedAt) return h('span', { class: 'badge badge--neutral' }, 'Arquivado');
  if (t.currentVersion && t.hasDraft) return h('span', { class: 'badge badge--warning' }, `v${t.currentVersion} · alterações não publicadas`);
  if (t.currentVersion) return h('span', { class: 'badge badge--success' }, `Publicado · v${t.currentVersion}`);
  return h('span', { class: 'badge badge--neutral' }, 'Rascunho');
}

function render() {
  const q = searchEl.value.trim().toLocaleLowerCase('pt-BR');
  const visible = items.filter((t) => !q || t.name.toLocaleLowerCase('pt-BR').includes(q));
  clear(listEl);

  if (visible.length === 0) {
    const empty = items.length === 0 && filter === 'active';
    listEl.append(h('div', { class: 'empty' },
      h('span', { class: 'empty__icon' }, icon(ICONS.doc)),
      h('h2', {}, empty ? 'Nenhum template ainda' : 'Nada encontrado'),
      h('p', {}, empty
        ? (canEdit ? 'Crie o primeiro modelo de contrato. Você cola o texto e marca as partes que mudam.'
          : EMBED ? 'Ainda não há modelos de contrato.' : 'Um Admin da sua empresa precisa criar os modelos de contrato.')
        : 'Tente outro termo ou troque o filtro.'),
      empty && canEdit ? h('button', { type: 'button', class: 'btn btn--primary', onclick: openNew }, 'Criar template') : null));
    return;
  }

  listEl.append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Template'), h('th', {}, 'Situação'), h('th', {}, 'Variáveis'), h('th', {}, 'Atualizado'))),
    h('tbody', {}, visible.map((t) => h('tr', {},
      h('td', { 'data-label': 'Template' },
        h('a', { class: 'row-link', href: href(`/template-editor.html?id=${t.id}`) }, t.name),
        t.description ? h('span', { class: 'cell-sub' }, t.description) : null),
      h('td', { 'data-label': 'Situação' }, statusBadge(t)),
      h('td', { 'data-label': 'Variáveis' }, String(t.variableCount)),
      h('td', { 'data-label': 'Atualizado' }, formatRelative(t.updatedAt)))))));
}

let ready = false;

async function load() {
  try {
    items = await api(`/templates?archived=${filter === 'archived'}`);
    render();
    if (!ready) { ready = true; notify('ready', { page: 'templates' }); }
  } catch (err) {
    clear(listEl).append(h('div', { class: 'panel__body' }, h('div', { class: 'alert alert--error' }, err.message)));
  }
}

let starters = null;

/** Modelos prontos; se a lista não vier, o modal oferece só o "em branco". */
async function loadStarters() {
  if (starters) return starters;
  try { starters = await api('/templates/starters'); } catch { return []; }
  return starters;
}

function startCard(value, title, description, checked) {
  return h('label', { class: 'radio-card' },
    h('input', { type: 'radio', name: 'starter', value, checked }),
    h('div', {}, h('strong', {}, title), h('span', {}, description)));
}

async function openNew() {
  const options = await loadStarters();
  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  const form = h('form', { class: 'form', id: 'new-template', novalidate: true },
    alertEl,
    h('fieldset', { class: 'fieldset field' },
      h('legend', { class: 'field__label' }, 'Começar com'),
      h('div', { class: 'radio-cards', 'data-starters': true },
        startCard('', 'Em branco', 'Escreva ou cole o seu próprio texto.', true),
        options.map((s) => startCard(s.id, s.name, `${s.description} ${s.variableCount} variáveis já cadastradas.`, false))),
      options.length ? h('p', { class: 'field__hint' }, 'Os modelos são um ponto de partida: revise o texto com o seu jurídico antes de publicar.') : null,
      h('p', { class: 'field__error', 'data-error-for': 'starterId' })),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 't-name' }, 'Nome'),
      h('input', { class: 'input', id: 't-name', name: 'name', maxlength: 120, required: true, placeholder: 'Ex.: Prestação de serviços' }),
      h('p', { class: 'field__error', 'data-error-for': 'name' })),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 't-desc' }, 'Descrição ', h('span', { class: 'muted' }, '(opcional)')),
      h('input', { class: 'input', id: 't-desc', name: 'description', maxlength: 500 }),
      h('p', { class: 'field__error', 'data-error-for': 'description' })));
  const submit = h('button', { type: 'submit', class: 'btn btn--primary', form: 'new-template' }, 'Criar e editar');
  const { close } = openModal({
    title: 'Novo template',
    description: options.length ? 'Comece de um modelo pronto ou do zero.' : null,
    body: form,
    actions: [h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Cancelar'), submit],
  });
  form.elements.name.focus();

  // Escolher um modelo sugere o nome dele, sem apagar o que a pessoa já digitou.
  let suggestedName = '';
  form.querySelector('[data-starters]').addEventListener('change', (e) => {
    const nameEl = form.elements.name;
    const starter = options.find((s) => s.id === e.target.value);
    const next = starter?.name ?? '';
    if (!nameEl.value.trim() || nameEl.value.trim() === suggestedName) nameEl.value = next;
    suggestedName = next;
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    const name = form.elements.name.value.trim();
    if (name.length < 2) { setFieldError(form, 'name', 'Informe o nome do template.'); form.elements.name.focus(); return; }
    setBusy(submit, true);
    try {
      const t = await api('/templates', {
        method: 'POST',
        body: {
          name,
          description: form.elements.description.value.trim() || null,
          starterId: form.elements.starter.value || null,
        },
      });
      notify('template.created', { template: { id: t.id, name: t.name } });
      location.assign(href(`/template-editor.html?id=${t.id}`));
    } catch (err) {
      setBusy(submit, false);
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    }
  });
}

for (const tab of document.querySelectorAll('[data-filter]')) {
  tab.addEventListener('click', () => {
    filter = tab.dataset.filter;
    for (const t of document.querySelectorAll('[data-filter]')) t.setAttribute('aria-selected', String(t === tab));
    load();
  });
}
searchEl.addEventListener('input', render);

if (canEdit) {
  const btn = document.querySelector('[data-new]');
  btn.hidden = false;
  btn.addEventListener('click', openNew);
}
load();
