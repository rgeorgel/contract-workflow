import { api } from '../api.js';
import { canEditContracts, isAdmin, requireUser } from '../auth.js';
import { h } from '../ui/dom.js';
import { contractNumber, formatRelative } from '../utils/format.js';
import { progress } from '../utils/status.js';

const STALE_DAYS = 3;
const me = await requireUser();
const summary = await api(`/dashboard/summary?staleDays=${STALE_DAYS}`);
const total = Object.values(summary.counts).reduce((a, b) => a + b, 0);

document.querySelector('[data-first-name]').textContent = me.name.split(' ')[0];
if (canEditContracts(me) && summary.hasPublishedTemplate) document.querySelector('[data-new]').hidden = false;

if (total === 0) {
  // Conta nova: mostra os primeiros passos em vez de números zerados.
  document.querySelector('[data-lead]').textContent = `A conta de ${me.organization.name} está pronta. Os próximos passos:`;
  const steps = [
    { title: 'Montar o primeiro template', text: 'Cole o modelo de contrato e marque as variáveis.', href: '/templates.html', done: summary.hasPublishedTemplate },
    { title: 'Gerar e enviar um contrato', text: 'Preencha os dados, adicione quem assina e envie.', href: summary.hasPublishedTemplate ? '/contract-new.html' : null },
    isAdmin(me)
      ? { title: 'Convidar a equipe', text: 'Defina quem é Admin, Editor ou Leitor.', href: '/users.html' }
      : { title: 'Conferir as configurações', text: 'Veja os padrões de envio da sua empresa.', href: '/settings.html' },
  ];
  const list = document.querySelector('[data-steps]');
  list.replaceChildren(...steps.map((s, i) => {
    const badge = s.done ? h('span', { class: 'badge badge--success' }, 'Feito')
      : s.href ? h('span', { class: 'badge badge--info' }, 'Abrir') : h('span', { class: 'badge badge--neutral' }, 'Depois do template');
    const content = [h('span', { class: 'step-badge' }, String(i + 1)), h('div', {}, h('h2', {}, s.title), h('p', { class: 'muted' }, s.text)), badge];
    return h('li', {}, s.href ? h('a', { class: 'card next-step', href: s.href }, content) : h('div', { class: 'card next-step' }, content));
  }));
  list.hidden = false;
} else {
  const c = summary.counts;
  document.querySelector('[data-lead]').textContent = summary.open
    ? `${summary.open} ${summary.open === 1 ? 'contrato aguarda' : 'contratos aguardam'} assinatura.`
    : 'Nenhum contrato aguardando assinatura.';

  const stat = (value, label, href, tone) => h('li', {},
    h('a', { class: 'card stat-card', href, 'data-tone': tone }, h('span', { class: 'stat__value' }, String(value)), h('span', { class: 'stat__label' }, label)));
  document.querySelector('[data-stats]').replaceChildren(
    stat(summary.open, 'Aguardando assinatura', '/contracts.html?status=Sent', summary.open ? 'warning' : null),
    stat(c.PartiallySigned, 'Parcialmente assinados', '/contracts.html?status=PartiallySigned'),
    stat(summary.completedThisMonth, 'Concluídos no mês', '/contracts.html?status=Completed', 'success'),
    stat(c.Draft, 'Rascunhos', '/contracts.html?status=Draft'),
    summary.sendErrors
      ? stat(summary.sendErrors, 'Com erro no envio', '/contracts.html?status=Sent', 'danger')
      : stat(c.Declined, 'Recusados', '/contracts.html?status=Declined', c.Declined ? 'danger' : null));

  document.querySelector('[data-stale-sub]').textContent = `Enviados há mais de ${summary.staleDays} dias, ainda sem todas as assinaturas.`;
  document.querySelector('[data-stale]').replaceChildren(summary.stale.length
    ? h('ul', { class: 'dash-list' }, summary.stale.map((s) => h('li', {},
      h('a', { href: `/contract.html?id=${s.id}` },
        h('span', { class: 'cell-number' }, contractNumber(s.number)),
        h('span', {}, h('strong', {}, s.title), h('br'), progress(s.signersSigned, s.signersTotal)),
        h('span', { class: 'days' }, `${s.days} dias`)))))
    : h('p', { class: 'dash-empty' }, 'Nada parado. Tudo andando no prazo.'));

  document.querySelector('[data-recent]').replaceChildren(summary.recentlyCompleted.length
    ? h('ul', { class: 'dash-list' }, summary.recentlyCompleted.map((r) => h('li', {},
      h('a', { href: `/contract.html?id=${r.id}` },
        h('span', { class: 'cell-number' }, contractNumber(r.number)),
        h('strong', {}, r.title),
        h('small', {}, formatRelative(r.completedAt))))))
    : h('p', { class: 'dash-empty' }, 'Os contratos concluídos aparecem aqui.'));
  document.querySelector('[data-overview]').hidden = false;
}

document.querySelector('[data-main]').hidden = false;
