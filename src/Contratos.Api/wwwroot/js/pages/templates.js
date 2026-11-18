import { api } from '../api.js';
import { requireUser } from '../auth.js';
import { can, EMBED, href, notify } from '../embed.js';
import { openNewTemplate as openNew } from './template-new.js';
import { clear, h, icon, ICONS } from '../ui/dom.js';
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
