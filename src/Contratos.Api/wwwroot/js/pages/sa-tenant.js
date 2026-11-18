import { api, ApiError } from '../api.js';
import { roleLabel } from '../auth.js';
import { requirePlatformAdmin } from '../superadmin/shell.js';
import { accessLine, chip, cycleLabel, formatDate, formatDateTime, meterRow, planBadge, plural, SA_ICONS, statusBadge } from '../superadmin/common.js';
import { clear, h, icon } from '../ui/dom.js';
import { applyErrors, clearErrors, setBusy, showAlert } from '../ui/form.js';
import { openModal } from '../ui/modal.js';
import { toast } from '../ui/toast.js';
import { formatMoney } from '../utils/billing.js';
import { formatRelative } from '../utils/format.js';

const me = await requirePlatformAdmin();

const content = document.querySelector('[data-content]');
const id = encodeURIComponent(new URLSearchParams(location.search).get('id') ?? '');

// ---------- Linha do tempo: rótulos ----------

const CATEGORIES = [
  ['', 'Tudo', SA_ICONS.all],
  ['conta', 'Conta', SA_ICONS.user],
  ['contratos', 'Contratos', SA_ICONS.doc],
  ['assinaturas', 'Assinaturas', SA_ICONS.pen],
  ['templates', 'Templates', SA_ICONS.layers],
  ['plano', 'Plano', SA_ICONS.coin],
  ['integracoes', 'Integrações', SA_ICONS.plug],
  ['superadmin', 'Superadmin', SA_ICONS.shield],
];
const CATEGORY_LABEL = Object.fromEntries(CATEGORIES.map(([k, l]) => [k, l]));
const CATEGORY_ICON = Object.fromEntries(CATEGORIES.map(([k, , i]) => [k, i]));

const p = (one, many) => (n) => plural(n, one, many);
const EVENT_LABELS = {
  OrganizationCreated: () => 'Conta criada',
  UserCreated: p('pessoa cadastrada', 'pessoas cadastradas'),
  UserInvited: p('convite enviado', 'convites enviados'),
  UserDeactivated: p('pessoa desativada', 'pessoas desativadas'),
  UserLastSeen: (n) => `Último acesso de ${plural(n, 'pessoa', 'pessoas')}`,
  Created: p('contrato criado', 'contratos criados'),
  ContractSent: p('contrato enviado para assinatura', 'contratos enviados para assinatura'),
  SendFailed: p('falha no envio', 'falhas no envio'),
  Completed: p('contrato concluído', 'contratos concluídos'),
  Cancelled: p('contrato cancelado', 'contratos cancelados'),
  Expired: p('contrato expirado', 'contratos expirados'),
  SignerViewed: p('visualização de signatário', 'visualizações de signatários'),
  SignerSigned: p('assinatura', 'assinaturas'),
  SignerDeclined: p('recusa de signatário', 'recusas de signatários'),
  DeliveryFailed: p('falha de entrega ao signatário', 'falhas de entrega aos signatários'),
  LinkResent: p('link reenviado', 'links reenviados'),
  TemplateCreated: p('template criado', 'templates criados'),
  TemplatePublished: p('versão de template publicada', 'versões de template publicadas'),
  TemplateArchived: p('template arquivado', 'templates arquivados'),
  CreditsPurchased: p('compra de créditos', 'compras de créditos'),
  CreditsWelcome: () => 'Créditos de boas-vindas',
  CreditsManual: p('concessão de créditos', 'concessões de créditos'),
  ApiKeyCreated: p('chave de API criada', 'chaves de API criadas'),
  ApiKeyRevoked: p('chave de API revogada', 'chaves de API revogadas'),
  WebhookCreated: p('webhook cadastrado', 'webhooks cadastrados'),
  EmbedOriginAdded: p('site autorizado no embed', 'sites autorizados no embed'),
  PlanChanged: () => 'Plano alterado pela equipe',
  CreditsGranted: () => 'Créditos concedidos pela equipe',
  Suspended: () => 'Conta suspensa',
  Reactivated: () => 'Conta reativada',
};
const AUDIT_LABELS = { PlanChanged: 'Plano alterado', CreditsGranted: 'Créditos concedidos', Suspended: 'Conta suspensa', Reactivated: 'Conta reativada' };

const eventTitle = (g) => (EVENT_LABELS[g.type] ?? ((n) => `${n}× ${g.type}`))(g.count);

// Os dias vêm agrupados no horário de Brasília: horas e "hoje" seguem o mesmo fuso, seja qual for o do navegador.
const TZ = 'America/Sao_Paulo';
const dayFmt = new Intl.DateTimeFormat('pt-BR', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
const timeFmt = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: TZ });
const isoDay = new Intl.DateTimeFormat('en-CA', { timeZone: TZ }); // AAAA-MM-DD
function dayLabel(day) {
  const d = new Date(`${day}T12:00:00Z`);
  const today = new Date(`${isoDay.format(new Date())}T12:00:00Z`);
  const diff = Math.round((today - d) / 86_400_000);
  return diff === 0 ? 'Hoje' : diff === 1 ? 'Ontem' : dayFmt.format(d);
}

// ---------- Estado ----------

let detail = null;
let tab = 'timeline';
let category = '';
let days = 30;

const panel = (title, ...body) => h('section', { class: 'panel' }, h('div', { class: 'panel__header' }, h('h2', {}, title)), ...body);
const kv = (rows) => h('dl', { class: 'kv' }, rows.filter(Boolean).map(([k, v]) => h('div', {}, h('dt', {}, k), h('dd', {}, v ?? '—'))));

function header(d) {
  const t = d.summary;
  const sub = d.billing.subscription;
  const live = sub && ['active', 'trialing', 'past_due'].includes(sub.status);
  const suspended = t.status === 'suspended';
  const action = (label, onclick, cls = 'btn--secondary', attrs = {}) =>
    h('button', { type: 'button', class: `btn ${cls} btn--sm`, onclick, ...attrs }, label);

  return [
    h('div', { class: 'tenant-head' },
      h('div', {},
        h('h1', {}, t.name),
        h('div', { class: 'tenant-head__badges' },
          planBadge(t.planId, t.planName), statusBadge(t.status),
          t.subscriptionInterval ? h('span', { class: 'badge badge--neutral' }, t.subscriptionInterval === 'year' ? 'Anual' : 'Mensal') : null),
        h('div', { class: 'tenant-head__meta' },
          h('span', {}, h('code', {}, t.slug)),
          h('span', {}, `Cadastro em ${formatDate(t.createdAt)}`),
          h('span', { class: 'access' }, accessLine(t.lastSeenAt)),
          t.owner ? h('span', {}, `${t.owner.name} · ${t.owner.email}`) : null)),
      h('div', { class: 'tenant-head__actions' },
        action('Trocar plano', openPlan, 'btn--secondary', live
          ? { disabled: true, title: 'Assinatura ativa no Stripe: o plano muda pelo portal de assinatura do cliente.' } : {}),
        action('Conceder créditos', openCredits),
        suspended
          ? action('Reativar conta', reactivate, 'btn--primary')
          : action('Suspender', openSuspend, 'btn--danger', t.id === me.organization.id
            ? { disabled: true, title: 'É a organização da sua própria conta.' } : {}))),
    suspended ? h('div', { class: 'alert alert--error suspended-banner', role: 'status' },
      h('div', { class: 'alert__body' },
        h('strong', {}, `Conta suspensa desde ${formatDateTime(t.suspendedAt)}.`),
        h('span', {}, d.suspensionReason ? `Motivo: ${d.suspensionReason}` : 'Sem motivo informado.'),
        h('span', {}, 'Ninguém da organização entra na tela, na API pública ou no embed até a reativação.'))) : null,
  ];
}

function tabs() {
  const btn = (key, label) => h('button', { type: 'button', role: 'tab', 'aria-selected': String(tab === key), onclick: () => { tab = key; render(); } }, label);
  return h('div', { class: 'tabs', role: 'tablist' }, btn('timeline', 'Linha do tempo'), btn('summary', 'Resumo'));
}

// ---------- Coluna lateral ----------

function sideColumn(d) {
  const t = d.summary;
  const b = d.billing;
  return h('div', { class: 'side-stack' },
    panel('Uso do plano', h('div', { class: 'panel__body side-meters' },
      b.contractsLimit > 0
        ? meterRow('Contratos no ciclo', b.contractsUsed, b.contractsLimit)
        : h('div', { class: 'meter-row__label' }, h('span', {}, 'Contratos no ciclo'), h('strong', {}, `${b.contractsUsed} (sem cota)`)),
      meterRow('Usuários', b.usersUsed, b.usersLimit),
      meterRow('Templates', b.templatesUsed, b.templatesLimit),
      kv([
        ['Ciclo', cycleLabel(b.periodStart, b.periodEnd)],
        ['Enviados com crédito', String(b.contractsFromCredits)],
        ['Créditos disponíveis', String(b.creditsAvailable)],
        ['Pode enviar agora', String(b.availableSends)],
      ]))),
    panel(`Usuários ${d.users.length}`, h('ul', { class: 'user-list' }, d.users.map((u) => h('li', {},
      h('div', { class: 'user-list__top' },
        h('strong', {}, u.name || '—'),
        h('span', { class: 'chips' },
          chip(roleLabel(u.role), u.role === 'Admin' ? 'success' : null),
          u.status !== 'Ativo' ? chip(u.status, u.status === 'Desativado' ? null : 'warning') : null)),
      h('span', { class: 'user-list__email' }, u.email),
      h('span', { class: 'user-list__seen' }, u.lastSeenAt
        ? `Último acesso: ${formatDateTime(u.lastSeenAt)} (${formatRelative(u.lastSeenAt)})`
        : `Nunca acessou · cadastro em ${formatDate(u.createdAt)}`))))),
    panel('Integrações', h('div', { class: 'panel__body' }, kv([
      ['Chaves de API ativas', String(t.integrations.apiKeys)],
      ['Webhooks', String(t.integrations.webhooks)],
      ['Sites no embed', String(t.integrations.embedOrigins)],
    ]))));
}

// ---------- Linha do tempo ----------

const timelineEl = h('div', {});

function timelineTools() {
  const chips = h('div', { class: 'filter-chips', role: 'group', 'aria-label': 'Categoria' },
    CATEGORIES.map(([key, label, svg]) => h('button', {
      type: 'button', 'aria-pressed': String(category === key),
      onclick: () => { category = key; render(); },
    }, icon(svg), label)));
  const period = h('select', { class: 'select', 'aria-label': 'Período' },
    [[7, 'Últimos 7 dias'], [30, 'Últimos 30 dias'], [90, 'Últimos 90 dias'], [365, 'Último ano']]
      .map(([v, l]) => h('option', { value: v, selected: v === days }, l)));
  period.addEventListener('change', () => { days = Number(period.value); loadTimeline(); });
  return [chips, h('div', { class: 'timeline-tools' }, h('span', { class: 'muted' }, 'Eventos agrupados por dia e tipo, no horário de Brasília.'), period)];
}

async function loadTimeline() {
  clear(timelineEl).append(h('div', { class: 'loading' }, h('div', { class: 'spinner', 'aria-label': 'Carregando' })));
  const q = new URLSearchParams({ days: String(days) });
  if (category) q.set('category', category);
  try {
    const groups = await api(`/superadmin/tenants/${id}/timeline?${q}`);
    if (!groups.length) {
      clear(timelineEl).append(h('div', { class: 'panel' }, h('div', { class: 'dash-empty' }, 'Nenhum evento neste período.')));
      return;
    }
    const list = h('div', { class: 'timeline' });
    let current = null;
    for (const g of groups) {
      if (g.day !== current) { current = g.day; list.append(h('div', { class: 'timeline__day' }, dayLabel(g.day))); }
      const meta = [CATEGORY_LABEL[g.category] ?? g.category, ...g.actors, ...(g.samples.length ? [g.samples.join(', ') + (g.count > g.samples.length && g.samples.length >= 3 ? '…' : '')] : [])];
      list.append(h('div', { class: 'timeline__item' },
        h('span', { class: 'timeline__icon', dataset: { cat: g.category } }, icon(CATEGORY_ICON[g.category] ?? SA_ICONS.all)),
        h('div', { class: 'timeline__card' },
          h('div', { class: 'timeline__title' }, h('span', {}, eventTitle(g)), h('time', { datetime: g.lastAt }, timeFmt.format(new Date(g.lastAt)))),
          h('div', { class: 'timeline__meta' }, meta.join(' · ')))));
    }
    clear(timelineEl).append(list);
  } catch (err) {
    clear(timelineEl).append(h('div', { class: 'alert alert--error' }, err.message));
  }
}

// ---------- Resumo ----------

function summaryTab(d) {
  const c = d.summary.contracts;
  const b = d.billing;
  const sub = b.subscription;
  return h('div', { class: 'side-stack' },
    panel('Contratos', h('div', { class: 'panel__body' }, kv([
      ['Total', String(c.total)],
      ['Criados nos últimos 30 dias', String(c.last30Days)],
      ['Abertos (aguardando assinatura)', String(c.open)],
      ['Concluídos', String(c.completed)],
      ['Recusados', String(c.declined)],
      ['Cancelados', String(c.cancelled)],
      ['Expirados', String(c.expired)],
      ['Rascunhos', String(c.draft)],
    ]))),
    panel('Plano e assinatura', h('div', { class: 'panel__body' }, kv([
      ['Plano', `${b.plan.name}${b.plan.monthlyPrice ? ` · ${formatMoney(b.plan.monthlyPrice)}/mês` : ''}`],
      ['Ciclo atual', cycleLabel(b.periodStart, b.periodEnd)],
      ['Assinatura no Stripe', sub ? sub.status : 'Nenhuma'],
      sub ? ['Cobrança', sub.interval === 'year' ? 'Anual' : 'Mensal'] : null,
      sub?.cancelAt ? ['Cancelamento agendado', formatDate(sub.cancelAt)] : null,
      d.stripeCustomerId ? ['Cliente no Stripe', h('code', {}, d.stripeCustomerId)] : null,
    ]))),
    panel('Créditos válidos', b.credits.length
      ? h('div', { class: 'table-wrap' }, h('table', { class: 'table' },
        h('thead', {}, h('tr', {}, ['Origem', 'Restam', 'Validade', 'Nota'].map((x) => h('th', {}, x)))),
        h('tbody', {}, b.credits.map((g) => h('tr', {},
          h('td', {}, { manual: 'Equipe', stripe: 'Compra', welcome: 'Boas-vindas' }[g.source] ?? g.source),
          h('td', {}, `${g.remaining}/${g.quantity}`),
          h('td', {}, formatDate(g.expiresAt)),
          h('td', {}, g.note ?? '—'))))))
      : h('div', { class: 'dash-empty' }, 'Nenhum crédito válido.')),
    panel('Templates', d.templates.length
      ? h('div', { class: 'table-wrap' }, h('table', { class: 'table' },
        h('thead', {}, h('tr', {}, ['Template', 'Contratos', 'Atualizado'].map((x) => h('th', {}, x)))),
        h('tbody', {}, d.templates.map((t) => h('tr', { class: t.archived ? 'is-muted' : null },
          h('td', {}, t.name, t.archived ? h('span', { class: 'cell-sub' }, 'Arquivado') : null),
          h('td', {}, String(t.contracts)),
          h('td', {}, formatDate(t.updatedAt)))))))
      : h('div', { class: 'dash-empty' }, 'Nenhum template.')),
    panel('Ações da equipe', d.audit.length
      ? h('ul', { class: 'audit-list' }, d.audit.map((a) => h('li', {},
        h('strong', {}, AUDIT_LABELS[a.action] ?? a.action),
        a.details ? h('span', {}, a.details) : null,
        h('small', {}, `${a.actorName} · ${formatDateTime(a.createdAt)}`))))
      : h('div', { class: 'dash-empty' }, 'Nenhuma ação registrada.')));
}

// ---------- Ações ----------

/** Formulário num modal; `submit` recebe os dados e devolve o detalhe atualizado. */
function formModal({ title, description, fields, submitLabel, danger = false, submit }) {
  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  const form = h('form', { class: 'form', novalidate: true, id: 'sa-form' }, alertEl, fields);
  const button = h('button', { type: 'submit', class: `btn ${danger ? 'btn--danger' : 'btn--primary'}`, form: 'sa-form' }, submitLabel);
  const { close } = openModal({
    title, description, body: form,
    actions: [h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Cancelar'), button],
  });
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    alertEl.hidden = true;
    setBusy(button, true);
    try {
      detail = await submit(form);
      close();
      render();
    } catch (err) {
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    } finally {
      setBusy(button, false);
    }
  });
  return form;
}

const field = (label, input, name, hint) => h('div', { class: 'field' },
  h('label', { class: 'field__label', for: input.id }, label), input,
  hint ? h('p', { class: 'field__hint' }, hint) : null,
  h('p', { class: 'field__error', 'data-error-for': name }));

function openPlan() {
  const b = detail.billing;
  const plans = h('fieldset', { class: 'field fieldset' },
    h('legend', { class: 'field__label' }, 'Plano'),
    h('div', { class: 'radio-cards' }, b.plans.map((pl) => h('label', { class: 'radio-card' },
      h('input', { type: 'radio', name: 'plan', value: pl.id, checked: pl.id === b.plan.id }),
      h('span', {}, h('strong', {}, pl.name),
        pl.monthlyPrice ? `${formatMoney(pl.monthlyPrice)}/mês · ${pl.contractsPerPeriod} contratos por ciclo` : 'Sem mensalidade, só créditos')))),
    h('p', { class: 'field__error', 'data-error-for': 'plan' }));
  const start = h('input', { class: 'input', type: 'date', id: 'sa-start', name: 'periodStart' });
  const end = h('input', { class: 'input', type: 'date', id: 'sa-end', name: 'periodEnd' });
  formModal({
    title: 'Trocar plano',
    description: 'Para contas sem assinatura no Stripe (venda manual, cortesia, ajuste). Fica registrado nas ações da equipe.',
    fields: [plans, h('div', { class: 'form-grid form-grid--2' },
      field('Início do ciclo (opcional)', start, 'periodStart'),
      field('Fim do ciclo (opcional)', end, 'periodEnd', 'Sem datas, a cota vale pelo mês do calendário.'))],
    submitLabel: 'Trocar plano',
    submit: async (form) => {
      const toIso = (v) => (v ? new Date(`${v}T03:00:00Z`).toISOString() : null); // meia-noite de Brasília
      const res = await api(`/superadmin/tenants/${id}/plan`, {
        method: 'PUT',
        body: { plan: form.elements.plan.value, periodStart: toIso(start.value), periodEnd: toIso(end.value) },
      });
      toast('Plano alterado.');
      return res;
    },
  });
}

function openCredits() {
  const qty = h('input', { class: 'input', type: 'number', id: 'sa-qty', name: 'quantity', min: 1, max: 10000, required: true, value: 10 });
  const months = h('input', { class: 'input', type: 'number', id: 'sa-months', name: 'validityMonths', min: 1, max: 36, value: 12 });
  const note = h('input', { class: 'input', type: 'text', id: 'sa-note', name: 'note', maxlength: 300, placeholder: 'Ex.: cortesia pelo atraso no suporte' });
  const form = formModal({
    title: 'Conceder créditos',
    description: `Contratos avulsos para ${detail.summary.name}, usados depois da cota do plano.`,
    fields: [h('div', { class: 'form-grid form-grid--2' },
      field('Quantidade', qty, 'quantity'),
      field('Validade (meses)', months, 'validityMonths')),
    field('Nota (opcional)', note, 'note', 'Aparece para o cliente em Configurações.')],
    submitLabel: 'Conceder',
    submit: async () => {
      const res = await api(`/superadmin/tenants/${id}/credits`, {
        method: 'POST',
        body: { quantity: Number(qty.value), validityMonths: Number(months.value), note: note.value.trim() || null },
      });
      toast(`${plural(Number(qty.value), 'crédito concedido', 'créditos concedidos')}.`);
      return res;
    },
  });
  form.elements.quantity.select();
}

function openSuspend() {
  const reason = h('textarea', { class: 'input', id: 'sa-reason', name: 'reason', maxlength: 300, placeholder: 'Ex.: inadimplência, uso indevido, pedido do cliente' });
  formModal({
    title: `Suspender ${detail.summary.name}?`,
    description: 'Ninguém da organização entra (tela, API pública e embed); sessões abertas caem em até 1 minuto. Contratos já enviados seguem com os signatários. Dá para reativar depois.',
    fields: [field('Motivo', reason, 'reason', 'Aparece só para a equipe da plataforma.')],
    submitLabel: 'Suspender conta',
    danger: true,
    submit: async () => {
      const res = await api(`/superadmin/tenants/${id}/suspend`, { method: 'POST', body: { reason: reason.value.trim() || null } });
      toast('Conta suspensa.');
      return res;
    },
  });
}

async function reactivate() {
  try {
    detail = await api(`/superadmin/tenants/${id}/reactivate`, { method: 'POST' });
    toast('Conta reativada.');
    render();
  } catch (err) {
    toast(err.message, 'error');
  }
}

// ---------- Montagem ----------

function render() {
  document.title = `${detail.summary.name} · Super admin`;
  const main = tab === 'timeline'
    ? h('div', { class: 'detail-layout' }, h('div', {}, timelineTools(), timelineEl), sideColumn(detail))
    : h('div', { class: 'detail-layout' }, summaryTab(detail), sideColumn(detail));
  clear(content).append(...header(detail).filter(Boolean), tabs(), main);
  if (tab === 'timeline') loadTimeline();
}

async function load() {
  if (!id) {
    clear(content).append(h('div', { class: 'alert alert--error' }, 'Organização não informada.'));
    return;
  }
  try {
    detail = await api(`/superadmin/tenants/${id}`);
    render();
  } catch (err) {
    clear(content).append(h('div', { class: 'alert alert--error' }, err.status === 404 ? 'Organização não encontrada.' : err.message));
  }
}

load();
