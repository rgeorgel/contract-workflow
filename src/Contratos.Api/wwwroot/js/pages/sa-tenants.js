import { api } from '../api.js';
import { requirePlatformAdmin } from '../superadmin/shell.js';
import {
  accessLine, chip, contractChips, cycleLabel, meterRow, PLAN_NAMES, planBadge, plural, SA_ICONS, sinceLabel, STATUS, statusBadge, tenantHref,
} from '../superadmin/common.js';
import { clear, h, icon } from '../ui/dom.js';
import { formatDate } from '../superadmin/common.js';

await requirePlatformAdmin();

const form = document.querySelector('[data-filters]');
const listEl = document.querySelector('[data-list]');
const viewEl = document.querySelector('[data-view]');
const PAGE_SIZE = 24;
const VIEW_KEY = 'sa.tenants.view';

const options = (select, entries) => select.replaceChildren(...entries.map(([value, label]) => h('option', { value }, label)));
options(form.elements.status, [['', 'Todos os status'], ...Object.entries(STATUS).map(([k, s]) => [k, s.label])]);
options(form.elements.plan, [['', 'Todos os planos'], ...Object.entries(PLAN_NAMES)]);
options(form.elements.activity, [['', 'Qualquer atividade'], ['active7', 'Acessou nos últimos 7 dias'], ['inactive30', 'Sem acesso há 30+ dias'], ['never', 'Nunca acessou']]);
options(form.elements.sort, [['', 'Último acesso: mais recente'], ['created', 'Cadastro: mais recente'], ['oldest', 'Cadastro: mais antigo'], ['contracts', 'Mais contratos (30 dias)'], ['name', 'Nome (A–Z)']]);

// Filtros na URL: dá para voltar e compartilhar o link.
const params = new URLSearchParams(location.search);
for (const name of ['q', 'status', 'plan', 'activity', 'sort']) form.elements[name].value = params.get(name) ?? '';
let page = Number(params.get('page')) || 1;

let view = 'cards';
try { view = localStorage.getItem(VIEW_KEY) === 'table' ? 'table' : 'cards'; } catch { /* sem storage */ }
const viewButtons = [['cards', 'Cartões', SA_ICONS.grid], ['table', 'Lista', SA_ICONS.list]].map(([value, label, svg]) =>
  h('button', { type: 'button', 'aria-label': label, title: label, 'aria-pressed': String(view === value), onclick: () => setView(value) }, icon(svg)));
viewEl.replaceChildren(...viewButtons);
let lastData = null;

function setView(value) {
  view = value;
  try { localStorage.setItem(VIEW_KEY, value); } catch { /* sem storage */ }
  viewButtons.forEach((b, i) => b.setAttribute('aria-pressed', String((i === 0 ? 'cards' : 'table') === value)));
  if (lastData) render(lastData);
}

// ---------- Cartão ----------

function usageBlock(t) {
  const u = t.usage;
  const contracts = u.contractsLimit > 0
    ? meterRow('Contratos no ciclo', u.contractsUsed, u.contractsLimit, { hint: `Ciclo: ${cycleLabel(u.periodStart, u.periodEnd)}` })
    : h('div', { class: 'meter-row__label' }, h('span', {}, 'Contratos no ciclo'), h('strong', {}, `${u.contractsUsed} (sem cota)`));
  return h('div', { class: 'tenant-card__section' },
    h('span', { class: 'sa-label' }, 'Uso do plano'),
    h('div', { class: 'tenant-card__cols' },
      meterRow('Usuários', u.usersUsed, u.usersLimit),
      meterRow('Templates', u.templatesUsed, u.templatesLimit)),
    contracts,
    h('div', { class: 'activity-row' }, h('span', {}, 'Créditos avulsos'), chip(plural(u.creditsAvailable, 'disponível', 'disponíveis'), u.creditsAvailable ? 'warning' : null, u.creditsAvailable)));
}

function card(t) {
  const tone = STATUS[t.status]?.tone;
  const i = t.integrations;
  return h('a', { class: 'tenant-card', href: tenantHref(t.id), dataset: tone ? { tone } : {} },
    h('div', { class: 'tenant-card__head' },
      h('div', {},
        h('div', { class: 'tenant-card__name' }, t.name),
        h('div', { class: 'tenant-card__since' }, sinceLabel(t.createdAt))),
      h('div', { class: 'tenant-card__badges' }, planBadge(t.planId, t.planName), statusBadge(t.status))),
    usageBlock(t),
    h('div', { class: 'tenant-card__section' },
      h('span', { class: 'sa-label' }, 'Atividade'),
      h('div', { class: 'activity-row' }, h('span', {}, 'Contratos'), h('span', {}, h('strong', {}, t.contracts.total), chip(`${t.contracts.last30Days} em 30 dias`, t.contracts.last30Days ? 'info' : null, t.contracts.last30Days))),
      contractChips(t.contracts)),
    h('div', { class: 'tenant-card__section tenant-card__cols' },
      h('div', { class: 'tenant-card__section-inner' },
        h('span', { class: 'sa-label' }, 'Integrações'),
        h('div', { class: 'chips' },
          chip(`API ${i.apiKeys}`, i.apiKeys ? 'info' : null, i.apiKeys, 'Chaves de API ativas'),
          chip(`Webhooks ${i.webhooks}`, i.webhooks ? 'info' : null, i.webhooks),
          chip(`Embed ${i.embedOrigins}`, i.embedOrigins ? 'info' : null, i.embedOrigins, 'Sites autorizados no embed'))),
      h('div', { class: 'access' },
        h('span', { class: 'sa-label' }, 'Acesso'),
        accessLine(t.lastSeenAt),
        t.subscriptionCancelAt ? h('span', { class: 'is-stale' }, `Cancela em ${formatDate(t.subscriptionCancelAt)}`) : null)),
    t.owner ? h('div', { class: 'owner-line', title: t.owner.email }, `${t.owner.name} · ${t.owner.email}`) : null);
}

// ---------- Lista ----------

function table(items) {
  return h('section', { class: 'panel' }, h('div', { class: 'table-wrap' },
    h('table', { class: 'table table--stack sa-table' },
      h('thead', {}, h('tr', {}, ['Organização', 'Plano', 'Contratos no ciclo', 'Usuários', 'Contratos', 'Último acesso'].map((c) => h('th', {}, c)))),
      h('tbody', {}, items.map((t) => h('tr', {},
        h('td', { 'data-label': 'Organização' },
          h('a', { class: 'row-link', href: tenantHref(t.id) }, t.name),
          h('span', { class: 'cell-sub' }, t.owner ? t.owner.email : sinceLabel(t.createdAt))),
        h('td', { 'data-label': 'Plano' }, h('span', { class: 'chips' }, planBadge(t.planId, t.planName), statusBadge(t.status))),
        h('td', { 'data-label': 'Contratos no ciclo' }, t.usage.contractsLimit
          ? meterRow('', t.usage.contractsUsed, t.usage.contractsLimit)
          : `${t.usage.contractsUsed} · ${plural(t.usage.creditsAvailable, 'crédito', 'créditos')}`),
        h('td', { 'data-label': 'Usuários' }, `${t.usage.usersUsed}/${t.usage.usersLimit ?? '∞'}`),
        h('td', { 'data-label': 'Contratos' }, h('strong', {}, t.contracts.total), h('span', { class: 'cell-sub' }, `${t.contracts.last30Days} em 30 dias`)),
        h('td', { 'data-label': 'Último acesso' }, h('span', { class: 'access' }, accessLine(t.lastSeenAt)))))))));
}

function pager(data) {
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize));
  if (pages <= 1) return null;
  const go = (p) => { page = p; load(); window.scrollTo({ top: 0 }); };
  return h('div', { class: 'pager' },
    h('span', {}, `Página ${data.page} de ${pages}`),
    h('span', { class: 'pager__buttons' },
      h('button', { type: 'button', class: 'btn btn--secondary btn--sm', disabled: data.page <= 1, onclick: () => go(data.page - 1) }, 'Anterior'),
      h('button', { type: 'button', class: 'btn btn--secondary btn--sm', disabled: data.page >= pages, onclick: () => go(data.page + 1) }, 'Próxima')));
}

function render(data) {
  lastData = data;
  if (!data.items.length) {
    clear(listEl).append(h('section', { class: 'panel' }, h('div', { class: 'empty' },
      h('h2', {}, 'Nenhuma organização encontrada'), h('p', {}, 'Mude a busca ou os filtros.'))));
    return;
  }
  clear(listEl).append(
    h('p', { class: 'sa-filters__count' }, plural(data.total, 'organização', 'organizações')),
    view === 'table' ? table(data.items) : h('div', { class: 'tenant-grid' }, data.items.map(card)),
    pager(data));
}

let controller = null;
async function load() {
  const query = new URLSearchParams();
  for (const name of ['q', 'status', 'plan', 'activity', 'sort']) {
    const v = form.elements[name].value.trim();
    if (v) query.set(name, v);
  }
  if (page > 1) query.set('page', String(page));
  history.replaceState(null, '', query.size ? `?${query}` : location.pathname);
  query.set('pageSize', String(PAGE_SIZE));

  controller?.abort();
  controller = new AbortController();
  try {
    render(await api(`/superadmin/tenants?${query}`, { signal: controller.signal }));
  } catch (err) {
    if (err.name === 'AbortError') return;
    clear(listEl).append(h('div', { class: 'alert alert--error' }, err.message));
  }
}

let typing = null;
form.elements.q.addEventListener('input', () => { clearTimeout(typing); typing = setTimeout(() => { page = 1; load(); }, 300); });
form.addEventListener('change', (e) => { if (e.target.name !== 'q') { page = 1; load(); } });
form.addEventListener('submit', (e) => { e.preventDefault(); page = 1; load(); });
load();
