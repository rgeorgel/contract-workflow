import { h } from '../ui/dom.js';

export const CONTRACT_STATUS = {
  Draft: { label: 'Rascunho', badge: 'neutral' },
  Sent: { label: 'Enviado', badge: 'info' },
  PartiallySigned: { label: 'Parcialmente assinado', badge: 'warning' },
  Completed: { label: 'Concluído', badge: 'success' },
  Declined: { label: 'Recusado', badge: 'danger' },
  Expired: { label: 'Expirado', badge: 'neutral' },
  Cancelled: { label: 'Cancelado', badge: 'neutral' },
};

export const SIGNER_STATUS = {
  Pending: { label: 'Pendente', badge: 'neutral' },
  Viewed: { label: 'Visualizou', badge: 'info' },
  Signed: { label: 'Assinou', badge: 'success' },
  Declined: { label: 'Recusou', badge: 'danger' },
  DeliveryFailed: { label: 'Falha na entrega', badge: 'danger' },
};

export const DELIVERY = { Email: 'E-mail', WhatsApp: 'WhatsApp', Sms: 'SMS' };
export const ACTIONS = { Sign: 'Assinar', SignAsWitness: 'Testemunha', Approve: 'Aprovar', Recognize: 'Reconhecer' };

/** Pendentes há mais que isso aparecem em destaque. */
export const STALE_DAYS = 3;

export function contractBadge(status) {
  const s = CONTRACT_STATUS[status] ?? { label: status, badge: 'neutral' };
  return h('span', { class: `badge badge--${s.badge}` }, s.label);
}

export function signerBadge(status) {
  const s = SIGNER_STATUS[status] ?? { label: status, badge: 'neutral' };
  return h('span', { class: `badge badge--${s.badge}` }, s.label);
}

export function progress(signed, total) {
  const pct = total ? Math.round((signed / total) * 100) : 0;
  return h('span', { class: 'mini-progress', title: `${signed} de ${total} assinaram` },
    h('span', { class: 'mini-progress__label' }, `${signed} de ${total}`),
    h('span', { class: 'mini-progress__track', 'aria-hidden': 'true' }, h('span', { class: 'mini-progress__bar', dataset: { pct } })));
}

/** Página de destino de um contrato: rascunho continua no assistente. */
export const contractHref = (c) => (c.status === 'Draft' ? `/contract-new.html?id=${c.id}` : `/contract.html?id=${c.id}`);
