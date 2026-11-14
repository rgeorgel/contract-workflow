import { api } from '../api.js';
import { canEditContracts, requireUser } from '../auth.js';
import { clear, h, icon, ICONS } from '../ui/dom.js';
import { contractNumber, formatDate } from '../utils/format.js';
import { CONTRACT_STATUS, contractBadge, contractHref, progress, STALE_DAYS } from '../utils/status.js';

const me = await requireUser();
const form = document.querySelector('[data-filters]');
const listEl = document.querySelector('[data-list]');
const pagerEl = document.querySelector('[data-pager]');
const PAGE_SIZE = 20;
const FILTERS = ['q', 'status', 'templateId', 'createdBy', 'dateField', 'from', 'to'];
let page = 1;
let controller = null;
let timer = null;

if (canEditContracts(me)) document.querySelector('[data-new]').hidden = false;

for (const [value, s] of Object.entries(CONTRACT_STATUS)) form.elements.status.append(h('option', { value }, s.label));
api('/templates').then((templates) => {
  for (const t of templates) form.elements.templateId.append(h('option', { value: t.id }, t.name));
  form.elements.templateId.value = new URLSearchParams(location.search).get('templateId') ?? '';
}).catch(() => {});
api('/organization/members').then((members) => {
  for (const m of members) form.elements.createdBy.append(h('option', { value: m.id }, m.name));
  form.elements.createdBy.value = new URLSearchParams(location.search).get('createdBy') ?? '';
}).catch(() => {});

// Filtros vivem na URL: o link pode ser compartilhado e o "voltar" funciona.
function readUrl() {
  const p = new URLSearchParams(location.search);
  for (const name of FILTERS) form.elements[name].value = p.get(name) ?? '';
  page = Number(p.get('page')) || 1;
}

function writeUrl() {
  const p = new URLSearchParams();
  for (const name of FILTERS) {
    const v = form.elements[name].value.trim();
    if (v) p.set(name, v);
  }
  if (page > 1) p.set('page', String(page));
  const qs = p.toString();
  history.replaceState(null, '', qs ? `?${qs}` : location.pathname);
  return p;
}

function flags(c) {
  if (c.sendError) return h('span', { class: 'flag flag--danger' }, 'Erro no envio');
  if (c.pendingDays !== null && c.pendingDays >= STALE_DAYS) return h('span', { class: 'flag' }, `Pendente há ${c.pendingDays} dias`);
  return null;
}

function render(result) {
  clear(listEl);
  if (result.total === 0) {
    const filtered = [...new URLSearchParams(location.search).keys()].length > 0;
    listEl.append(h('div', { class: 'empty' },
      h('span', { class: 'empty__icon' }, icon(ICONS.doc)),
      h('h2', {}, filtered ? 'Nenhum contrato encontrado' : 'Nenhum contrato ainda'),
      h('p', {}, filtered ? 'Tente outros termos ou limpe os filtros.' : 'Crie um contrato a partir de um template publicado.'),
      !filtered && canEditContracts(me) ? h('a', { class: 'btn btn--primary', href: '/contract-new.html' }, 'Novo contrato') : null));
    pagerEl.hidden = true;
    return;
  }

  listEl.append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Nº'), h('th', {}, 'Contrato'), h('th', {}, 'Status'), h('th', {}, 'Assinaturas'), h('th', {}, 'Criado em'))),
    h('tbody', {}, result.items.map((c) => h('tr', {},
      h('td', { class: 'cell-number', 'data-label': 'Nº' }, contractNumber(c.number)),
      h('td', { 'data-label': 'Contrato' },
        h('a', { class: 'row-link', href: contractHref(c) }, c.title),
        h('span', { class: 'cell-sub' }, c.templateName)),
      h('td', { 'data-label': 'Status' }, contractBadge(c.status), h('br'), flags(c)),
      h('td', { 'data-label': 'Assinaturas' }, c.signersTotal ? progress(c.signersSigned, c.signersTotal) : h('span', { class: 'muted' }, '—')),
      h('td', { 'data-label': 'Criado em' }, formatDate(c.createdAt)))))));

  const start = (result.page - 1) * result.pageSize + 1;
  const end = Math.min(result.page * result.pageSize, result.total);
  document.querySelector('[data-range]').textContent = `${start}–${end} de ${result.total}`;
  document.querySelector('[data-prev]').disabled = result.page <= 1;
  document.querySelector('[data-next]').disabled = end >= result.total;
  pagerEl.hidden = false;
}

async function load() {
  const params = writeUrl();
  params.set('page', String(page));
  params.set('pageSize', String(PAGE_SIZE));
  controller?.abort();
  controller = new AbortController();
  try {
    render(await api(`/contracts?${params}`, { signal: controller.signal }));
  } catch (err) {
    if (err.name === 'AbortError') return;
    clear(listEl).append(h('div', { class: 'panel__body' }, h('div', { class: 'alert alert--error' }, err.message)));
  }
}

form.addEventListener('input', (e) => {
  page = 1;
  clearTimeout(timer);
  timer = setTimeout(load, e.target.name === 'q' ? 300 : 0);
});
form.addEventListener('submit', (e) => e.preventDefault());
document.querySelector('[data-prev]').addEventListener('click', () => { page -= 1; load(); });
document.querySelector('[data-next]').addEventListener('click', () => { page += 1; load(); });

readUrl();
load();
