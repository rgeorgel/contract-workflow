import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { clear, h } from '../ui/dom.js';
import { setBusy } from '../ui/form.js';
import { confirmModal, openModal } from '../ui/modal.js';
import { toast } from '../ui/toast.js';
import { contractNumber, formatDate, formatDateTime, formatRelative } from '../utils/format.js';
import { ACTIONS, contractBadge, DELIVERY, signerBadge } from '../utils/status.js';

await requireUser();
const id = new URLSearchParams(location.search).get('id');
if (!id) location.replace('/contracts.html');

const POLL_MS = 15_000;
const alertEl = document.querySelector('[data-alert]');
let contract = null;
let pollTimer = null;

// ---------- Linha do tempo ----------

const EVENT_TEXT = {
  Created: (e) => ['Rascunho criado', `a partir do template ${e.payload.template ?? ''}`.trim(), 'neutral'],
  SignerAdded: (e) => [`${e.signerName ?? 'Signatário'} adicionado`, null, 'neutral'],
  SignerRemoved: (e) => [`${e.payload.signerName ?? 'Signatário'} removido`, null, 'neutral'],
  ContractSent: () => ['Enviado para assinatura', 'PDF gerado e congelado', 'info'],
  SentToProvider: () => ['Links entregues ao provedor de assinatura', null, 'info'],
  SendFailed: (e) => ['Erro no envio', e.payload.error, 'danger'],
  SendRetried: () => ['Envio repetido', null, 'info'],
  ReturnedToDraft: () => ['Voltou para rascunho para correção', null, 'info'],
  SignerViewed: (e) => [`${e.signerName} abriu o documento`, null, 'warning'],
  SignerSigned: (e) => [`${e.signerName} assinou`, null, 'success'],
  SignerDeclined: (e) => [`${e.signerName} recusou`, e.payload.reason, 'danger'],
  DeliveryFailed: (e) => [`Falha ao entregar o link a ${e.signerName}`, e.payload.channel ? `Canal: ${DELIVERY[e.payload.channel] ?? e.payload.channel}` : null, 'danger'],
  LinkResent: (e) => [e.payload?.auto === 'true'
    ? `Lembrete automático: link reenviado a ${e.signerName}, com o prazo perto`
    : `Link reenviado a ${e.signerName}`, null, 'info'],
  Completed: () => ['Todos assinaram', 'Contrato concluído', 'success'],
  SignedDocumentStored: () => ['PDF assinado arquivado', null, 'success'],
  Cancelled: () => ['Contrato cancelado', null, 'danger'],
  Expired: () => ['Prazo encerrado', 'O contrato expirou sem todas as assinaturas', 'neutral'],
  ProviderDocumentDeleted: () => ['Documento removido no provedor', 'A exclusão não partiu deste sistema', 'danger'],
};

async function renderTimeline() {
  const events = await api(`/contracts/${id}/events`);
  document.querySelector('[data-timeline]').replaceChildren(...events.map((e) => {
    const [title, detail, tone] = (EVENT_TEXT[e.type] ?? (() => [e.type, null, 'neutral']))(e);
    return h('li', { 'data-tone': tone },
      h('strong', {}, title),
      detail ? h('div', { class: 'muted' }, detail) : null,
      h('time', { datetime: e.occurredAt, title: formatDateTime(e.occurredAt) },
        formatRelative(e.occurredAt), e.actorName ? ` · ${e.actorName}` : ''));
  }));
}

// ---------- Contrato ----------

function signerMeta(s) {
  const parts = [s.role, s.action !== 'Sign' ? ACTIONS[s.action] : null].filter(Boolean);
  return `${parts.length ? `${parts.join(' · ')} · ` : ''}${DELIVERY[s.deliveryMethod]}: ${s.deliveryMethod === 'Email' ? s.email : s.phone}`;
}

function signerWhen(s) {
  if (s.signedAt) return `Assinou ${formatRelative(s.signedAt)}`;
  if (s.declinedAt) return `Recusou ${formatRelative(s.declinedAt)}`;
  if (s.viewedAt) return `Abriu ${formatRelative(s.viewedAt)}`;
  return null;
}

function renderSigners() {
  const sequential = contract.signingMode === 'Sequential';
  const list = document.querySelector('[data-signers]');
  clear(list).append(...contract.signers.map((s, i) => h('li', { class: 'signer-row' },
    h('span', { class: 'signer-card__order' }, sequential ? String(i + 1) : '•'),
    h('div', { class: 'signer-row__main' },
      h('strong', {}, s.name),
      h('span', {}, signerMeta(s)),
      s.declineReason ? h('span', {}, `Motivo: ${s.declineReason}`) : null,
      s.canResend && contract.permissions.canResend
        ? h('span', { class: 'signer-row__actions' }, h('button', { type: 'button', class: 'btn btn--link', onclick: (e) => resend(s, e.currentTarget) }, 'Reenviar link'))
        : null),
    h('div', { class: 'signer-row__side' },
      signerBadge(s.status),
      signerWhen(s) ? h('small', { title: formatDateTime(s.signedAt ?? s.declinedAt ?? s.viewedAt) }, signerWhen(s)) : null),
    contract.permissions.canSimulate && ['Pending', 'Viewed', 'DeliveryFailed'].includes(s.status)
      ? h('div', { class: 'simulate' },
        h('strong', {}, 'Simular (provedor fake):'),
        ['view', 'sign', 'decline', 'fail']
          .filter((a) => !(a === 'view' && s.status === 'Viewed') && !(a === 'fail' && s.status !== 'Pending'))
          .map((a) => h('button', { type: 'button', onclick: (e) => simulate(s, a, e.currentTarget) },
            { view: 'Abrir', sign: 'Assinar', decline: 'Recusar', fail: 'Falha na entrega' }[a])))
      : null)));

  const total = contract.signers.length;
  const signed = contract.signers.filter((s) => s.status === 'Signed').length;
  const pct = total ? Math.round((signed / total) * 100) : 0;
  document.querySelector('[data-progress]').replaceChildren(h('div', { class: 'progress-lg' },
    h('div', { class: 'progress-lg__label' },
      h('span', {}, `${signed} de ${total} ${total === 1 ? 'assinou' : 'assinaram'}`),
      h('span', { class: 'muted' }, sequential ? 'Assinatura em ordem' : 'Todos ao mesmo tempo')),
    h('div', { class: 'progress-lg__track', 'aria-hidden': 'true' }, h('span', { class: 'progress-lg__bar', dataset: { pct } }))));
}

function displayValue(v, raw) {
  if (!raw) return '—';
  if (v.type === 'Date') return formatDate(`${raw}T12:00:00`);
  if (v.type === 'Money') return `R$ ${raw}`;
  return raw;
}

function renderFacts() {
  const c = contract;
  const row = (label, value) => (value ? [h('dt', {}, label), h('dd', {}, value)] : []);
  document.querySelector('[data-facts]').replaceChildren(
    ...row('Template', `${c.template.name} · v${c.template.version}`),
    ...row('Criado', `${formatDateTime(c.createdAt)}${c.createdByName ? ` por ${c.createdByName}` : ''}`),
    ...row('Enviado', c.sentAt && formatDateTime(c.sentAt)),
    ...row('Concluído', c.completedAt && formatDateTime(c.completedAt)),
    ...row('Cancelado', c.cancelledAt && formatDateTime(c.cancelledAt)),
    ...row('Prazo', c.expiresAt && formatDate(c.expiresAt)),
    ...row('Mensagem', c.message),
    ...row('SHA-256 do PDF', c.documentSha256 && h('code', { title: 'Impressão digital do PDF enviado' }, c.documentSha256)));

  const values = document.querySelector('[data-values]');
  clear(values);
  for (const v of c.template.variables) values.append(h('dt', {}, v.label), h('dd', {}, displayValue(v, c.variableValues[v.key])));
  if (!c.template.variables.length) values.append(h('dd', { class: 'muted' }, 'Sem variáveis.'));
}

function renderBanner() {
  const banner = document.querySelector('[data-banner]');
  clear(banner);
  if (contract.sendError) {
    const p = contract.permissions;
    banner.append(h('div', { class: 'alert alert--error banner' }, h('div', { class: 'alert__body' },
      h('strong', {}, 'Erro no envio'),
      h('span', {}, contract.sendError),
      p.canResend || p.canReturnToDraft
        ? h('div', { class: 'banner__actions' },
          p.canReturnToDraft
            ? h('button', { type: 'button', class: 'btn btn--primary btn--sm', onclick: (e) => returnToDraft(e.currentTarget) }, 'Voltar para rascunho e corrigir')
            : null,
          p.canResend
            ? h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: (e) => retrySend(e.currentTarget) }, 'Tentar de novo')
            : null)
        : null)));
  } else if (contract.status === 'Sent' && !contract.signers.some((s) => s.status !== 'Pending' || s.canResend) && contract.permissions.canSimulate === false && contract.permissions.canResend) {
    banner.append(h('div', { class: 'alert alert--info banner' }, 'Entregando os links ao provedor de assinatura…'));
  }
}

function renderActions() {
  const c = contract;
  const actions = document.querySelector('[data-actions]');
  clear(actions);
  if (c.status === 'Draft' && c.permissions.canEdit)
    actions.append(h('a', { class: 'btn btn--primary', href: `/contract-new.html?id=${c.id}&step=2` }, 'Continuar edição'));
  if (c.hasDocument) actions.append(h('a', { class: 'btn btn--secondary', href: `/api/v1/contracts/${c.id}/document` }, 'PDF original'));
  if (c.hasSignedDocument) actions.append(h('a', { class: 'btn btn--primary', href: `/api/v1/contracts/${c.id}/signed-document` }, 'PDF assinado'));
  if (c.permissions.canCancel) actions.append(h('button', { type: 'button', class: 'btn btn--ghost', onclick: cancel }, 'Cancelar contrato'));
  if (c.permissions.canDelete) actions.append(h('button', { type: 'button', class: 'btn btn--ghost is-danger-text', onclick: remove }, 'Excluir'));
}

function render() {
  const c = contract;
  document.title = `${c.title} · Contratos`;
  document.querySelector('[data-number]').textContent = `Nº ${contractNumber(c.number)}`;
  document.querySelector('[data-title]').textContent = c.title;
  document.querySelector('[data-status]').replaceChildren(contractBadge(c.status));
  document.querySelector('[data-subtitle]').textContent = c.sentAt
    ? `Enviado ${formatRelative(c.sentAt)}${c.expiresAt ? ` · prazo até ${formatDate(c.expiresAt)}` : ''}`
    : 'Rascunho: ainda não foi enviado.';
  renderActions();
  renderBanner();
  renderSigners();
  renderFacts();
}

async function load() {
  try {
    contract = await api(`/contracts/${id}`);
    render();
    await renderTimeline();
    document.querySelector('[data-main]').hidden = false;
  } catch (err) {
    document.querySelector('[data-main]').hidden = false;
    showError(err.status === 404 ? new Error('Contrato não encontrado. Ele pode ter sido excluído.') : err);
  }
  schedulePoll();
}

/** Atualiza a cada 15 s enquanto o contrato espera assinaturas (e só com a aba visível). */
function schedulePoll() {
  clearTimeout(pollTimer);
  if (!contract || !['Sent', 'PartiallySigned'].includes(contract.status)) return;
  pollTimer = setTimeout(() => { if (!document.hidden) load(); else schedulePoll(); }, POLL_MS);
}
document.addEventListener('visibilitychange', () => { if (!document.hidden && contract) load(); });

function showError(err) {
  alertEl.textContent = err.message || 'Algo deu errado.';
  alertEl.hidden = false;
}

// ---------- Ações ----------

async function act(button, fn, success) {
  if (button) setBusy(button, true);
  try {
    await fn();
    if (success) toast(success);
    await load();
  } catch (err) {
    toast(err.message, 'error');
  } finally {
    if (button?.isConnected) setBusy(button, false);
  }
}

const resend = (s, btn) => act(btn, () => api(`/contracts/${id}/signers/${s.id}/resend`, { method: 'POST' }), `Link reenviado para ${s.name}.`);
async function returnToDraft(btn) {
  setBusy(btn, true);
  try {
    await api(`/contracts/${id}/return-to-draft`, { method: 'POST' });
    // Os erros do provedor costumam ser de signatário: abre direto nessa etapa.
    location.assign(`/contract-new.html?id=${id}&step=3`);
  } catch (err) {
    setBusy(btn, false);
    toast(err.message, 'error');
  }
}

const retrySend = (btn) => act(btn, () => api(`/contracts/${id}/retry-send`, { method: 'POST' }), 'Envio repetido.');
const simulate = (s, action, btn) => act(btn, () => api(`/dev/fake-provider/contracts/${id}/signers/${s.id}/${action}`, { method: 'POST' }));

async function cancel() {
  const sent = contract.status !== 'Draft';
  const ok = await confirmModal({
    title: 'Cancelar este contrato?',
    description: sent
      ? 'Os links param de funcionar e o documento é removido no provedor de assinatura. O histórico continua aqui.'
      : 'O rascunho fica guardado como cancelado.',
    confirmLabel: 'Cancelar contrato', danger: true,
  });
  if (ok) act(null, () => api(`/contracts/${id}/cancel`, { method: 'POST' }), 'Contrato cancelado.');
}

function remove() {
  const number = contract.number;
  const input = h('input', { class: 'input', id: 'confirm-number', inputmode: 'numeric', autocomplete: 'off' });
  const also = h('input', { type: 'checkbox', id: 'also-provider', checked: true });
  const submit = h('button', { type: 'button', class: 'btn btn--danger', disabled: true }, 'Excluir para sempre');
  const body = h('div', { class: 'form' },
    h('p', {}, 'Remove o contrato, os signatários, a linha do tempo e os PDFs. Fica só um registro de que ele existiu. Não dá para desfazer.'),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'confirm-number' }, `Digite ${number} para confirmar`),
      input),
    contract.sentAt ? h('label', { class: 'check' }, also, ' Excluir também no provedor de assinatura') : null);
  input.addEventListener('input', () => { submit.disabled = Number(input.value.replace(/^0+/, '')) !== number; });

  const { close } = openModal({
    title: `Excluir o contrato nº ${contractNumber(number)}?`,
    body,
    actions: [h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Voltar'), submit],
  });
  input.focus();
  submit.addEventListener('click', async () => {
    setBusy(submit, true);
    try {
      await api(`/contracts/${id}?confirmNumber=${number}&alsoInAutentique=${Boolean(contract.sentAt && also.checked)}`, { method: 'DELETE' });
      close();
      toast('Contrato excluído.');
      location.assign('/contracts.html');
    } catch (err) {
      setBusy(submit, false);
      toast(err instanceof ApiError ? err.message : 'Não foi possível excluir.', 'error');
    }
  });
}

load();
