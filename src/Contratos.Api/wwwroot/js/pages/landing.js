import { h } from '../ui/dom.js';
import { formatMoney, limitLabel } from '../utils/billing.js';

// Borda no cabeçalho ao rolar e ano corrente no rodapé.
const header = document.querySelector('[data-header]');
const onScroll = () => header?.classList.toggle('is-scrolled', window.scrollY > 8);
window.addEventListener('scroll', onScroll, { passive: true });
onScroll();

const year = document.querySelector('[data-year]');
if (year) year.textContent = String(new Date().getFullYear());

// ---------- Preços ----------
// Vêm do mesmo catálogo que aplica os limites no sistema (/api/v1/plans): página e produto não divergem.


const TAGLINES = {
  avulso: 'Para quem envia de vez em quando: sem mensalidade.',
  essencial: 'Para quem começou a enviar contrato toda semana.',
  profissional: 'Para equipes que vivem de contrato.',
  empresa: 'Para alto volume e equipes grandes.',
};
const RECOMMENDED = 'profissional';
const CHANNELS = { Email: 'e-mail', WhatsApp: 'WhatsApp', Sms: 'SMS' };

const channelText = (channels) => {
  const names = channels.map((c) => CHANNELS[c] ?? c);
  const list = names.length > 1 ? `${names.slice(0, -1).join(', ')} e ${names.at(-1)}` : names[0];
  return `Envio por ${list}`;
};

let period = 'monthly';
let catalog = null;

function priceBlock(plan) {
  if (plan.monthlyPrice === 0) {
    const best = catalog.packs.reduce((a, p) => (p.unitPrice < a.unitPrice ? p : a));
    return [h('p', { class: 'price' }, h('strong', {}, formatMoney(best.unitPrice)), h('span', {}, '/contrato')),
      h('p', { class: 'price__note' }, `no pacote de ${best.quantity}; sem mensalidade`)];
  }
  if (period === 'annual') {
    return [h('p', { class: 'price' }, h('strong', {}, formatMoney(Math.round((plan.annualPrice / 12) * 100) / 100)), h('span', {}, '/mês')),
      h('p', { class: 'price__note' }, `cobrado ${formatMoney(plan.annualPrice)} por ano`)];
  }
  return [h('p', { class: 'price' }, h('strong', {}, formatMoney(plan.monthlyPrice)), h('span', {}, '/mês')),
    h('p', { class: 'price__note' }, `ou ${formatMoney(plan.annualPrice)} por ano`)];
}

function renderPricing() {
  document.querySelector('[data-pricing]').replaceChildren(...catalog.plans.map((plan) => {
    const recommended = plan.id === RECOMMENDED;
    return h('article', { class: `price-card${recommended ? ' price-card--featured' : ''}`, 'aria-labelledby': `plano-${plan.id}` },
      recommended ? h('span', { class: 'price-card__flag' }, 'Recomendado') : null,
      h('h3', { id: `plano-${plan.id}` }, plan.name),
      h('p', { class: 'price-card__tagline' }, TAGLINES[plan.id] ?? ''),
      ...priceBlock(plan),
      h('a', { class: `btn ${recommended ? 'btn--primary' : 'btn--secondary'} price-card__cta`, href: '/signup.html' },
        plan.monthlyPrice === 0 ? `Testar com ${catalog.welcomeCredits} grátis` : 'Começar agora'),
      h('ul', { class: 'price-card__features' },
        plan.contractsPerPeriod > 0
          ? h('li', {}, h('span', {}, h('strong', {}, `${plan.contractsPerPeriod} contratos`), ' por mês'))
          : h('li', {}, h('span', {}, h('strong', {}, `${catalog.welcomeCredits} contratos grátis`), ` para testar (${catalog.welcomeCreditDays} dias)`)),
        h('li', {}, h('span', {}, limitLabel(plan.maxUsers, 'usuário', 'usuários'))),
        h('li', {}, h('span', {}, limitLabel(plan.maxTemplates, 'template', 'templates'))),
        h('li', {}, h('span', {}, channelText(plan.channels))),
        plan.bulkSend ? h('li', {}, h('span', {}, 'Envio em lote por planilha')) : null,
        plan.apiAccess ? h('li', {}, h('span', {}, 'API e webhooks (CRM, ERP)')) : null));
  }));
}

function renderPacks() {
  const cheapest = Math.min(...catalog.packs.map((p) => p.unitPrice));
  document.querySelector('[data-validity]').textContent = String(catalog.creditValidityMonths);
  document.querySelector('[data-pack-list]').replaceChildren(...catalog.packs.map((p) => h('li', { class: 'pack' },
    h('strong', {}, `${p.quantity} ${p.quantity === 1 ? 'contrato' : 'contratos'}`),
    h('span', { class: 'pack__price' }, formatMoney(p.price)),
    h('span', { class: `pack__unit${p.unitPrice === cheapest ? ' is-best' : ''}` }, `${formatMoney(p.unitPrice)} cada`))));
  document.querySelector('[data-packs]').hidden = false;
}

const toggle = document.querySelector('[data-billing-toggle]');
toggle?.addEventListener('click', (e) => {
  const button = e.target.closest('[data-period]');
  if (!button || !catalog) return;
  period = button.dataset.period;
  for (const b of toggle.querySelectorAll('[data-period]')) b.setAttribute('aria-pressed', String(b === button));
  renderPricing();
});

try {
  const res = await fetch('/api/v1/plans', { headers: { Accept: 'application/json' } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  catalog = await res.json();
  renderPricing();
  renderPacks();
  toggle.hidden = false;
} catch {
  // Fica a mensagem de reserva, com o link para criar a conta.
}
