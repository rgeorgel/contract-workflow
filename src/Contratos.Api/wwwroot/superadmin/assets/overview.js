import { api } from './api.js';
import { accessLine, barChart, formatDate, formatMoney, PLAN_NAMES, pct, planBadge, plural, STATUS, statusBadge, tenantHref } from './common.js';
import { h, clear } from './lib/dom.js';
import { requirePlatformAdmin } from './shell.js';

await requirePlatformAdmin();

const content = document.querySelector('[data-content]');

const stat = (value, label, hint, href) => h('li', {},
  h(href ? 'a' : 'div', { class: 'card sa-stat stat-card', href: href ?? null },
    h('span', { class: 'sa-stat__value' }, value),
    h('span', { class: 'sa-stat__label' }, label),
    hint ? h('span', { class: 'sa-stat__hint' }, hint) : null));

const panel = (title, ...body) => h('section', { class: 'panel' },
  h('div', { class: 'panel__header' }, h('h2', {}, title)), ...body);

function distribution(rows, labelOf, total, hrefOf) {
  return h('div', { class: 'panel__body dist' }, rows.map((r) => h('div', { class: 'dist__row' },
    h('a', { href: hrefOf(r.key) }, labelOf(r.key)),
    h('span', { class: 'mini-progress__track', 'aria-hidden': 'true' }, h('span', { class: 'mini-progress__bar', dataset: { pct: pct(r.count, total) } })),
    h('span', {}, r.count))));
}

function tenantList(items, empty) {
  if (!items.length) return h('div', { class: 'dash-empty' }, empty);
  return h('ul', { class: 'sa-list' }, items.map((t) => h('li', {},
    h('a', { href: tenantHref(t.id) },
      h('span', {},
        h('strong', {}, t.name),
        h('small', {}, `desde ${formatDate(t.createdAt)} · `, accessLine(t.lastSeenAt))),
      h('span', { class: 'chips' }, planBadge(t.planId, t.planName), statusBadge(t.status))))));
}

function render(o) {
  const sum = (points) => points.reduce((a, p) => a + p.count, 0);
  const diff = o.contractsThisMonth - o.contractsLastMonth;
  clear(content).append(
    h('ul', { class: 'sa-stats' },
      stat(o.tenants.toLocaleString('pt-BR'), 'Organizações', `${o.newLast7Days} novas em 7 dias · ${o.newLast30Days} em 30`, '/superadmin/tenants.html?sort=created'),
      stat(o.activeLast7Days.toLocaleString('pt-BR'), 'Ativas em 7 dias', `${o.inactiveOver30Days} sem acesso há 30+ dias`, '/superadmin/tenants.html?activity=active7'),
      stat(formatMoney(o.mrr), 'Receita recorrente (MRR)', plural(o.paidSubscriptions, 'assinatura paga', 'assinaturas pagas'), '/superadmin/tenants.html?status=active'),
      stat(o.contractsThisMonth.toLocaleString('pt-BR'), 'Contratos enviados no mês',
        `${diff >= 0 ? '+' : ''}${diff} vs. mês passado · ${o.completedThisMonth} concluídos`)),
    h('div', { class: 'sa-grid sa-grid--2' },
      panel('Cadastros nos últimos 30 dias', h('div', { class: 'panel__body' },
        h('div', { class: 'chart-total' }, h('span', {}, 'Novas organizações'), h('strong', {}, sum(o.signupsByDay))),
        barChart(o.signupsByDay, { label: 'Cadastros por dia' }))),
      panel('Contratos enviados nos últimos 30 dias', h('div', { class: 'panel__body' },
        h('div', { class: 'chart-total' }, h('span', {}, 'Todas as organizações'), h('strong', {}, sum(o.contractsByDay))),
        barChart(o.contractsByDay, { label: 'Contratos enviados por dia' }))),
      panel('Por plano', distribution(o.byPlan, (k) => PLAN_NAMES[k] ?? k, o.tenants, (k) => `/superadmin/tenants.html?plan=${k}`)),
      panel('Por situação', distribution(o.byStatus, (k) => STATUS[k]?.label ?? k, o.tenants, (k) => `/superadmin/tenants.html?status=${k}`)),
      panel('Pedem atenção', h('p', { class: 'sa-hint' }, 'Pagamento pendente, suspensas ou com cancelamento agendado.'),
        tenantList(o.needsAttention, 'Nada pendente.')),
      panel('Cadastros recentes', tenantList(o.newest, 'Nenhuma organização ainda.'))));
}

try {
  render(await api('/overview'));
} catch (err) {
  clear(content).append(h('div', { class: 'alert alert--error' }, err.message));
}
