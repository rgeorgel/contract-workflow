import { api, ApiError } from '../api.js';
import { isAdmin, requireUser } from '../auth.js';
import { applyErrors, clearErrors, hideAlert, setBusy, showAlert } from '../ui/form.js';
import { toast } from '../ui/toast.js';
import { h } from '../ui/dom.js';
import { formatDate } from '../utils/format.js';
import { channelsLabel, formatMoney, limitLabel, pct, renewsLabel } from '../utils/billing.js';

const me = await requireUser();
const form = document.getElementById('settings-form');
const alertEl = form.querySelector('[data-form-alert]');

function fill(org) {
  form.elements.name.value = org.name;
  form.elements.reminder.value = org.settings.reminder ?? '';
  form.elements.language.value = org.settings.language;
  form.elements.defaultMessage.value = org.settings.defaultMessage ?? '';
  form.elements.staleAlertDays.value = String(org.settings.staleAlertDays);
  form.elements.expiryAlertDays.value = String(org.settings.expiryAlertDays);
  form.elements.autoResendBeforeExpiry.checked = org.settings.autoResendBeforeExpiry;
}

// ---------- Plano e uso ----------

/** `alert`: muda de cor perto do limite (só para a cota de contratos; usuários no limite é normal). */
function meter(label, used, limit, hint, alert = false) {
  const value = limit == null ? `${used} · ilimitado` : `${used} de ${limit}`;
  const p = pct(used, limit);
  return h('div', { class: 'meter' },
    h('div', { class: 'meter__label' }, h('span', {}, label), h('strong', {}, value)),
    limit == null ? null : h('div', {
      class: 'mini-progress__track meter__track', role: 'progressbar',
      'aria-label': label, 'aria-valuemin': '0', 'aria-valuemax': String(limit), 'aria-valuenow': String(Math.min(used, limit)),
    }, h('span', { class: `mini-progress__bar meter__bar${!alert ? ' is-calm' : p >= 100 ? ' is-full' : p >= 80 ? ' is-warn' : ''}`, dataset: { pct: p } })),
    hint ? h('p', { class: 'meter__hint' }, hint) : null);
}

function contractsHint(b) {
  const left = Math.max(0, b.contractsLimit - b.contractsUsed);
  const renew = b.subscription?.cancelAt
    ? `O ciclo termina em ${renewsLabel(b.periodEnd)}; depois a conta passa para o Avulso, que usa só créditos.`
    : `Renova em ${renewsLabel(b.periodEnd)}.`;
  if (left > 0) return `${left} ${left === 1 ? 'restante' : 'restantes'} neste ciclo. ${renew}`;
  if (b.creditsAvailable > 0) return `Os contratos do plano acabaram neste ciclo; os próximos envios usam créditos avulsos. ${renew}`;
  return `Os contratos do plano acabaram neste ciclo e não há créditos avulsos: novos envios ficam bloqueados. ${renew}`;
}

// ---------- Assinatura (Stripe) ----------

let interval = 'month';
let lastBilling = null;
const canPay = (b) => b.onlineCheckout && isAdmin(me);
const live = (b) => ['active', 'trialing', 'past_due'].includes(b.subscription?.status);

/** Abre o checkout ou o portal do Stripe; a página sai daqui. */
async function goTo(path, body, button) {
  setBusy(button, true);
  try {
    const { url } = await api(path, { method: 'POST', body });
    location.assign(url);
  } catch (err) {
    setBusy(button, false);
    toast(err.message, 'error');
  }
}

function subscriptionNotice(b) {
  const s = b.subscription;
  if (b.paymentOverdue) {
    return h('div', { class: 'alert alert--error billing-notice', role: 'alert' },
      h('div', { class: 'alert__body' },
        h('strong', {}, 'O pagamento da assinatura não foi aprovado.'),
        h('span', {}, 'Enquanto ele estiver pendente, criar e enviar contratos, criar templates e convidar usuários fica bloqueado. Os contratos existentes continuam disponíveis.'),
        h('span', {}, isAdmin(me)
          ? 'Atualize o cartão e pague a fatura em "Gerenciar assinatura"; tudo volta a funcionar assim que o pagamento for aprovado.'
          : 'Peça a um Admin da sua empresa para atualizar o cartão.')));
  }
  if (!s || !live(b)) return null;
  if (s.cancelAt) {
    return h('div', { class: 'alert alert--warning billing-notice' },
      `Assinatura cancelada: o plano ${b.plan.name} vale até ${renewsLabel(s.cancelAt)} e depois a conta passa para o Avulso, que usa só créditos. `
      + 'Mudou de ideia? Reative em "Gerenciar assinatura".');
  }
  return null;
}

function planAction(b, p) {
  if (!canPay(b) || p.monthlyPrice === 0) return null;
  if (live(b)) {
    if (p.id === b.plan.id) return null;
    const btn = h('button', { type: 'button', class: 'btn btn--secondary btn--sm plan-card__cta' }, 'Mudar para este plano');
    btn.addEventListener('click', () => goTo('/billing/portal', undefined, btn));
    return btn;
  }
  const btn = h('button', { type: 'button', class: `btn btn--sm plan-card__cta ${p.id === 'profissional' ? 'btn--primary' : 'btn--secondary'}` },
    `Assinar ${interval === 'year' ? 'anual' : 'mensal'}`);
  btn.addEventListener('click', () => goTo('/billing/checkout', { plan: p.id, interval }, btn));
  return btn;
}

function renderBilling(b) {
  lastBilling = b;
  const badge = document.querySelector('[data-plan-badge]');
  badge.textContent = `Plano ${b.plan.name}`;
  badge.hidden = false;

  const credits = b.credits.length
    ? h('ul', { class: 'credit-list' }, b.credits.map((g) => h('li', {},
      h('strong', {}, `${g.remaining} de ${g.quantity}`), ` · vencem em ${formatDate(g.expiresAt)}`, g.note ? h('span', { class: 'muted' }, ` · ${g.note}`) : null)))
    : h('p', { class: 'meter__hint' }, 'Nenhum crédito avulso.');

  const manage = canPay(b) && b.subscription
    ? h('button', { type: 'button', class: 'btn btn--secondary btn--sm' }, 'Gerenciar assinatura')
    : null;
  manage?.addEventListener('click', () => goTo('/billing/portal', undefined, manage));
  const subLine = live(b) && b.subscription.interval
    ? ` · assinatura ${b.subscription.interval === 'year' ? 'anual' : 'mensal'}${
      b.subscription.cancelAt ? `, cancela em ${renewsLabel(b.subscription.cancelAt)}`
        : b.subscription.currentPeriodEnd ? `, renova em ${renewsLabel(b.subscription.currentPeriodEnd)}` : ''}`
    : '';

  document.querySelector('[data-billing]').replaceChildren(...[
    subscriptionNotice(b),
    h('div', { class: 'billing-head' }, h('p', { class: 'billing-plan' },
      h('strong', {}, `Plano ${b.plan.name}`), ' · ',
      b.plan.monthlyPrice > 0 ? `${formatMoney(b.plan.monthlyPrice)}/mês` : 'sem mensalidade, 1 crédito por contrato',
      ' · ', `envio por ${channelsLabel(b.plan.channels)}`, subLine), manage),
    h('div', { class: 'meter-grid' },
      // Avulso não tem cota mensal: os créditos são tudo.
      b.contractsLimit > 0 ? meter('Contratos do plano neste ciclo', b.contractsUsed, b.contractsLimit, contractsHint(b), true) : null,
      h('div', { class: 'meter' },
        h('div', { class: 'meter__label' },
          h('span', {}, b.contractsLimit > 0 ? 'Créditos avulsos' : 'Créditos de contrato'),
          h('strong', { 'data-credits': '' }, String(b.creditsAvailable))),
        b.contractsLimit === 0
          ? h('p', { class: 'meter__hint' }, b.creditsAvailable > 0
            ? 'Cada contrato enviado usa 1 crédito.'
            : 'Sem créditos, novos envios ficam bloqueados. Compre um pacote ou assine um plano abaixo.')
          : null,
        b.contractsFromCredits ? h('p', { class: 'meter__hint' }, `${b.contractsFromCredits} ${b.contractsFromCredits === 1 ? 'usado' : 'usados'} neste ciclo.`) : null,
        credits),
      meter('Usuários', b.usersUsed, b.usersLimit, 'Conta usuários ativos e convites pendentes.'),
      meter('Templates ativos', b.templatesUsed, b.templatesLimit, b.templatesLimit == null ? null : 'Templates arquivados não contam.')),
  ].filter(Boolean));

  document.querySelector('[data-plans]').replaceChildren(...b.plans.map((p) => {
    const current = p.id === b.plan.id;
    return h('article', { class: `plan-card${current ? ' is-current' : ''}` },
      h('header', { class: 'plan-card__header' },
        h('h3', {}, p.name),
        current ? h('span', { class: 'badge badge--success' }, 'Seu plano') : null),
      ...cardPrice(b, p),
      h('ul', { class: 'plan-card__features' },
        p.contractsPerPeriod > 0
          ? h('li', {}, `${p.contractsPerPeriod} contratos por mês`)
          : h('li', {}, `Sem mensalidade: compre contratos a partir de ${formatMoney(Math.min(...b.packs.map((k) => k.unitPrice)))}`),
        h('li', {}, limitLabel(p.maxUsers, 'usuário', 'usuários')),
        h('li', {}, limitLabel(p.maxTemplates, 'template', 'templates')),
        h('li', {}, `Envio por ${channelsLabel(p.channels)}`),
        p.bulkSend ? h('li', {}, 'Envio em lote por planilha') : null),
      planAction(b, p));
  }));

  const toggle = document.querySelector('[data-interval]');
  toggle.hidden = !(canPay(b) && !live(b));

  document.querySelector('[data-pack-actions]').hidden = !canPay(b);
  document.querySelector('[data-packs]').replaceChildren(...b.packs.map((p) => {
    const label = `${p.quantity} ${p.quantity === 1 ? 'contrato' : 'contratos'}`;
    let buy = null;
    if (canPay(b)) {
      buy = h('button', { type: 'button', class: 'btn btn--secondary btn--sm', 'aria-label': `Comprar pacote de ${label}` }, 'Comprar');
      buy.addEventListener('click', () => goTo('/billing/packs/checkout', { quantity: p.quantity }, buy));
    }
    return h('tr', {},
      h('td', {}, label),
      h('td', {}, formatMoney(p.price)),
      h('td', {}, formatMoney(p.unitPrice)),
      buy ? h('td', { class: 'pack-table__action' }, buy) : null);
  }));

  const contact = document.querySelector('[data-contact]');
  if (b.onlineCheckout) {
    contact.replaceChildren(isAdmin(me)
      ? 'Pagamento por cartão de crédito, processado pelo Stripe. Troque de plano, de cartão ou cancele quando quiser em "Gerenciar assinatura".'
      : 'Só um Admin da sua empresa pode assinar ou mudar de plano.');
    return;
  }
  contact.replaceChildren(...(b.contactEmail
    ? ['Para mudar de plano ou comprar contratos avulsos, escreva para ', h('a', { href: `mailto:${b.contactEmail}` }, b.contactEmail), '.']
    : ['A contratação pelo sistema chega em breve.']));
}

function cardPrice(b, p) {
  if (p.monthlyPrice === 0) return [h('p', { class: 'plan-card__price' }, h('strong', {}, formatMoney(0)), '/mês'), h('p', { class: 'plan-card__annual' }, 'paga só o que enviar')];
  if (interval === 'year' && canPay(b) && !live(b)) {
    return [h('p', { class: 'plan-card__price' }, h('strong', {}, formatMoney(Math.round((p.annualPrice / 12) * 100) / 100)), '/mês'),
      h('p', { class: 'plan-card__annual' }, `cobrado ${formatMoney(p.annualPrice)} por ano`)];
  }
  return [h('p', { class: 'plan-card__price' }, h('strong', {}, formatMoney(p.monthlyPrice)), '/mês'),
    h('p', { class: 'plan-card__annual' }, `ou ${formatMoney(p.annualPrice)}/ano (2 meses grátis)`)];
}

document.querySelector('[data-interval]').addEventListener('click', (e) => {
  const button = e.target.closest('[data-interval-value]');
  if (!button || !lastBilling) return;
  interval = button.dataset.intervalValue;
  for (const b of e.currentTarget.querySelectorAll('[data-interval-value]')) b.setAttribute('aria-selected', String(b === button));
  renderBilling(lastBilling);
});

/** Volta do Stripe Checkout: ativa o plano (ou libera o pacote) na hora; o webhook confirma de novo depois. */
async function handleCheckoutReturn() {
  const params = new URLSearchParams(location.search);
  const pack = params.get('pack');
  if (pack) {
    history.replaceState(null, '', location.pathname + location.hash);
    if (pack === 'cancelled') { toast('Compra cancelada. Nada foi cobrado.'); return null; }
    try {
      const before = await api('/organization/billing');
      const b = await api('/billing/packs/confirm', { method: 'POST', body: { sessionId: params.get('session_id') } });
      const added = b.creditsAvailable - before.creditsAvailable;
      toast(added > 0 ? `Pacote liberado: ${added} ${added === 1 ? 'contrato' : 'contratos'} a mais.` : 'Pagamento recebido. Os contratos entram assim que o Stripe confirmar.');
      return b;
    } catch (err) {
      toast(err.message, 'error');
      return null;
    }
  }
  const result = params.get('checkout');
  if (!result) return null;
  history.replaceState(null, '', location.pathname);
  if (result === 'cancelled') { toast('Pagamento cancelado. Nada foi cobrado.'); return null; }
  try {
    const b = await api('/billing/checkout/confirm', { method: 'POST', body: { sessionId: params.get('session_id') } });
    if (live(b)) toast(`Assinatura ativada: plano ${b.plan.name}.`);
    else toast('Pagamento recebido. O plano é ativado assim que o Stripe confirmar.');
    return b;
  } catch (err) {
    toast(err.message, 'error');
    return null;
  }
}

const [org, billing, confirmed] = await Promise.all([api('/organization/settings'), api('/organization/billing'), handleCheckoutReturn()]);
fill(org);
renderBilling(confirmed ?? billing);

if (!isAdmin(me)) {
  for (const el of form.elements) el.disabled = true;
  form.querySelector('[data-actions]').hidden = true;
  form.querySelector('[data-readonly-note]').hidden = false;
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(alertEl);
  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    const org = await api('/organization/settings', {
      method: 'PUT',
      body: {
        name: form.elements.name.value.trim(),
        reminder: form.elements.reminder.value || null,
        language: form.elements.language.value,
        defaultMessage: form.elements.defaultMessage.value.trim() || null,
        staleAlertDays: Number(form.elements.staleAlertDays.value || 0),
        expiryAlertDays: Number(form.elements.expiryAlertDays.value || 0),
        autoResendBeforeExpiry: form.elements.autoResendBeforeExpiry.checked,
      },
    });
    fill(org);
    toast('Configurações salvas.');
  } catch (err) {
    if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
  } finally {
    setBusy(button, false);
  }
});
