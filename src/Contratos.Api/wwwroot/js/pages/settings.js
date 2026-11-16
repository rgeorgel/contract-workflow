import { api, ApiError } from '../api.js';
import { isAdmin, requireUser } from '../auth.js';
import { applyErrors, clearErrors, hideAlert, setBusy, showAlert } from '../ui/form.js';
import { toast } from '../ui/toast.js';
import { h } from '../ui/dom.js';
import { formatDate } from '../utils/format.js';
import { channelsLabel, formatMoney, limitLabel, pct, periodEndLabel, renewsLabel } from '../utils/billing.js';

const me = await requireUser();
const form = document.getElementById('settings-form');
const alertEl = form.querySelector('[data-form-alert]');

function fill(org) {
  form.elements.name.value = org.name;
  form.elements.reminder.value = org.settings.reminder ?? '';
  form.elements.language.value = org.settings.language;
  form.elements.defaultMessage.value = org.settings.defaultMessage ?? '';
}

// ---------- Plano e uso ----------

function meter(label, used, limit, hint) {
  const value = limit == null ? `${used} · ilimitado` : `${used} de ${limit}`;
  const p = pct(used, limit);
  return h('div', { class: 'meter' },
    h('div', { class: 'meter__label' }, h('span', {}, label), h('strong', {}, value)),
    limit == null ? null : h('div', {
      class: 'mini-progress__track meter__track', role: 'progressbar',
      'aria-label': label, 'aria-valuemin': '0', 'aria-valuemax': String(limit), 'aria-valuenow': String(Math.min(used, limit)),
    }, h('span', { class: `mini-progress__bar meter__bar${p >= 100 ? ' is-full' : p >= 80 ? ' is-warn' : ''}`, dataset: { pct: p } })),
    hint ? h('p', { class: 'meter__hint' }, hint) : null);
}

function contractsHint(b) {
  const left = Math.max(0, b.contractsLimit - b.contractsUsed);
  const renew = `Renova em ${renewsLabel(b.periodEnd)}.`;
  if (left > 0) return `${left} ${left === 1 ? 'restante' : 'restantes'} neste ciclo (até ${periodEndLabel(b.periodEnd)}). ${renew}`;
  if (b.creditsAvailable > 0) return `Os contratos do plano acabaram neste ciclo; os próximos envios usam créditos avulsos. ${renew}`;
  return `Os contratos do plano acabaram neste ciclo e não há créditos avulsos: novos envios ficam bloqueados. ${renew}`;
}

function renderBilling(b) {
  const badge = document.querySelector('[data-plan-badge]');
  badge.textContent = `Plano ${b.plan.name}`;
  badge.hidden = false;

  const credits = b.credits.length
    ? h('ul', { class: 'credit-list' }, b.credits.map((g) => h('li', {},
      h('strong', {}, `${g.remaining} de ${g.quantity}`), ` · vencem em ${formatDate(g.expiresAt)}`, g.note ? h('span', { class: 'muted' }, ` · ${g.note}`) : null)))
    : h('p', { class: 'meter__hint' }, 'Nenhum crédito avulso.');

  document.querySelector('[data-billing]').replaceChildren(
    h('p', { class: 'billing-plan' },
      h('strong', {}, `Plano ${b.plan.name}`), ' · ',
      b.plan.monthlyPrice > 0 ? `${formatMoney(b.plan.monthlyPrice)}/mês` : 'gratuito',
      ' · ', `envio por ${channelsLabel(b.plan.channels)}`),
    h('div', { class: 'meter-grid' },
      meter('Contratos do plano neste ciclo', b.contractsUsed, b.contractsLimit, contractsHint(b)),
      h('div', { class: 'meter' },
        h('div', { class: 'meter__label' }, h('span', {}, 'Créditos avulsos'), h('strong', { 'data-credits': '' }, String(b.creditsAvailable))),
        b.contractsFromCredits ? h('p', { class: 'meter__hint' }, `${b.contractsFromCredits} ${b.contractsFromCredits === 1 ? 'usado' : 'usados'} neste ciclo.`) : null,
        credits),
      meter('Usuários', b.usersUsed, b.usersLimit, 'Conta usuários ativos e convites pendentes.'),
      meter('Templates ativos', b.templatesUsed, b.templatesLimit, b.templatesLimit == null ? null : 'Templates arquivados não contam.')));

  document.querySelector('[data-plans]').replaceChildren(...b.plans.map((p) => {
    const current = p.id === b.plan.id;
    return h('article', { class: `plan-card${current ? ' is-current' : ''}` },
      h('header', { class: 'plan-card__header' },
        h('h3', {}, p.name),
        current ? h('span', { class: 'badge badge--success' }, 'Seu plano') : null),
      h('p', { class: 'plan-card__price' },
        h('strong', {}, formatMoney(p.monthlyPrice)), '/mês'),
      p.annualPrice > 0 ? h('p', { class: 'plan-card__annual' }, `ou ${formatMoney(p.annualPrice)}/ano (2 meses grátis)`) : h('p', { class: 'plan-card__annual' }, 'para sempre'),
      h('ul', { class: 'plan-card__features' },
        h('li', {}, `${p.contractsPerPeriod} contratos por mês`),
        h('li', {}, limitLabel(p.maxUsers, 'usuário', 'usuários')),
        h('li', {}, limitLabel(p.maxTemplates, 'template', 'templates')),
        h('li', {}, `Envio por ${channelsLabel(p.channels)}`)));
  }));

  document.querySelector('[data-packs]').replaceChildren(...b.packs.map((p) => h('tr', {},
    h('td', {}, `${p.quantity} ${p.quantity === 1 ? 'contrato' : 'contratos'}`),
    h('td', {}, formatMoney(p.price)),
    h('td', {}, formatMoney(p.unitPrice)))));

  const contact = document.querySelector('[data-contact]');
  contact.replaceChildren(...(b.contactEmail
    ? ['Para mudar de plano ou comprar contratos avulsos, escreva para ', h('a', { href: `mailto:${b.contactEmail}` }, b.contactEmail), '.']
    : ['A contratação pelo sistema chega em breve.']));
}

const [org, billing] = await Promise.all([api('/organization/settings'), api('/organization/billing')]);
fill(org);
renderBilling(billing);

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
