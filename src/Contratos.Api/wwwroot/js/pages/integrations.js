import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { clear, h } from '../ui/dom.js';
import { applyErrors, clearErrors, setBusy, showAlert } from '../ui/form.js';
import { confirmModal, openModal } from '../ui/modal.js';
import { toast } from '../ui/toast.js';
import { formatDateTime } from '../utils/format.js';

await requireUser({ roles: ['Admin'] });

const main = document.querySelector('[data-main]');
const keysEl = document.querySelector('[data-keys]');
const hooksEl = document.querySelector('[data-hooks]');
const deliveriesEl = document.querySelector('[data-deliveries]');
const filterEl = document.querySelector('[data-delivery-filter]');
const pagerEl = document.querySelector('[data-pager]');
const newKeyBtn = document.querySelector('[data-new-key]');
const newHookBtn = document.querySelector('[data-new-hook]');
const originsEl = document.querySelector('[data-origins]');
const originForm = document.querySelector('[data-origin-form]');

let overview = null;
let page = 1;

const DELIVERY_STATUS = {
  succeeded: ['Entregue', 'badge--success'],
  pending: ['Na fila', 'badge--info'],
  failed: ['Falhou', 'badge--danger'],
};

const linkBtn = (label, onclick, danger = false) =>
  h('button', { type: 'button', class: `btn btn--link${danger ? ' is-danger' : ''}`, onclick }, label);

const errorBox = (err) => h('div', { class: 'panel__body' }, h('div', { class: 'alert alert--error' }, err.message));

/** Campo só leitura com botão de copiar: chave ou segredo que a pessoa precisa levar para o outro sistema. */
function secretField(id, label, value, hint) {
  const input = h('input', { class: 'input input--mono', id, type: 'text', readonly: true, value, spellcheck: 'false' });
  const copy = h('button', {
    type: 'button', class: 'btn btn--secondary',
    onclick: async () => {
      input.select();
      try {
        await navigator.clipboard.writeText(value);
        copy.textContent = 'Copiado';
      } catch {
        document.execCommand?.('copy');
        copy.textContent = 'Copiado';
      }
      setTimeout(() => { copy.textContent = 'Copiar'; }, 2000);
    },
  }, 'Copiar');
  return h('div', { class: 'field' },
    h('label', { class: 'field__label', for: id }, label),
    h('div', { class: 'secret-box' }, input, copy),
    hint ? h('p', { class: 'field__hint' }, hint) : null);
}

// ---------- Chaves ----------

function renderKeys() {
  const keys = overview.keys;
  if (keys.length === 0) {
    clear(keysEl).append(h('div', { class: 'panel__body' },
      h('p', { class: 'muted' }, 'Nenhuma chave ainda. Crie uma para cada sistema que vai falar com a API (CRM, ERP, site).')));
    return;
  }
  const rows = keys.map((k) => h('tr', { class: k.revokedAt ? 'is-muted' : null },
    h('td', { 'data-label': 'Nome' }, h('span', { class: 'cell-main' }, k.name)),
    h('td', { 'data-label': 'Chave' }, h('code', { class: 'key-prefix' }, `${k.prefix}…`)),
    h('td', { 'data-label': 'Criada' },
      h('span', { class: 'cell-main' }, formatDateTime(k.createdAt)),
      k.createdByName ? h('span', { class: 'cell-sub' }, `por ${k.createdByName}`) : null),
    h('td', { 'data-label': 'Último uso' }, k.lastUsedAt ? formatDateTime(k.lastUsedAt) : h('span', { class: 'muted' }, 'Nunca')),
    h('td', { class: 'cell-actions' }, k.revokedAt
      ? h('span', { class: 'badge badge--neutral' }, 'Revogada')
      : linkBtn('Revogar', () => revokeKey(k), true))));

  clear(keysEl).append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Nome'), h('th', {}, 'Chave'), h('th', {}, 'Criada'), h('th', {}, 'Último uso'),
      h('th', {}, h('span', { class: 'visually-hidden' }, 'Ações')))),
    h('tbody', {}, rows)));
}

async function revokeKey(key) {
  const ok = await confirmModal({
    title: `Revogar a chave “${key.name}”?`,
    description: 'Ela para de funcionar na hora: o sistema que a usa passa a receber 401. Não dá para desfazer.',
    confirmLabel: 'Revogar', danger: true,
  });
  if (!ok) return;
  try {
    await api(`/integrations/keys/${key.id}`, { method: 'DELETE' });
    toast('Chave revogada.');
    await load();
  } catch (err) { toast(err.message, 'error'); }
}

function openNewKey() {
  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  const form = h('form', { class: 'form', novalidate: true, id: 'key-form' },
    alertEl,
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'key-name' }, 'Nome'),
      h('input', { class: 'input', id: 'key-name', name: 'name', type: 'text', maxlength: 80, required: true, placeholder: 'Ex.: CRM, ERP, site', 'aria-describedby': 'key-name-hint' }),
      h('p', { class: 'field__hint', id: 'key-name-hint' }, 'Para saber qual sistema usa a chave e revogar só ela, se precisar.'),
      h('p', { class: 'field__error', 'data-error-for': 'name' })));
  const submit = h('button', { type: 'submit', class: 'btn btn--primary', form: 'key-form' }, 'Criar chave');
  const cancel = h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => modal.close() }, 'Cancelar');
  const modal = openModal({
    title: 'Nova chave de API',
    description: 'A chave age como um Editor: cria, envia e cancela contratos. Não mexe em usuários nem em configurações.',
    body: form,
    actions: [cancel, submit],
  });
  form.elements.name.focus();

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    alertEl.hidden = true;
    setBusy(submit, true);
    try {
      const created = await api('/integrations/keys', { method: 'POST', body: { name: form.elements.name.value.trim() } });
      // Troca o formulário pela chave: é a única vez que ela aparece.
      form.replaceChildren(
        h('div', { class: 'alert alert--warning' }, h('strong', {}, 'Copie a chave agora. '), 'Por segurança, ela não aparece de novo. Se perder, revogue e crie outra.'),
        secretField('new-key', `Chave “${created.key.name}”`, created.token, 'Guarde num cofre de segredos ou numa variável de ambiente do seu servidor, nunca no código do site.'));
      submit.remove();
      cancel.textContent = 'Já copiei';
      cancel.classList.replace('btn--secondary', 'btn--primary');
      form.querySelector('input').select();
      await load();
    } catch (err) {
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    } finally {
      setBusy(submit, false);
    }
  });
}

// ---------- Webhooks ----------

const eventsSummary = (events) => {
  if (events.length === 0) return 'Todos os eventos';
  return events.length === 1 ? events[0] : `${events.length} eventos`;
};

function hookStatus(w) {
  if (!w.enabled) return h('span', { class: 'badge badge--neutral' }, 'Desativado');
  if (w.failureStreak > 0) return h('span', { class: 'badge badge--danger', title: 'Entregas recentes esgotaram as tentativas' }, 'Falhando');
  return h('span', { class: 'badge badge--success' }, 'Ativo');
}

function renderHooks() {
  const hooks = overview.endpoints;
  if (hooks.length === 0) {
    clear(hooksEl).append(h('div', { class: 'panel__body' },
      h('p', { class: 'muted' }, 'Nenhum webhook ainda. Cadastre o endereço do seu sistema para saber, na hora, quando um contrato é assinado.')));
    return;
  }
  const rows = hooks.map((w) => h('tr', {},
    h('td', { 'data-label': 'Endereço' },
      h('span', { class: 'cell-main cell-url' }, w.url),
      w.description ? h('span', { class: 'cell-sub' }, w.description) : null),
    h('td', { 'data-label': 'Eventos' }, h('span', { title: w.events.join(', ') || 'Todos' }, eventsSummary(w.events))),
    h('td', { 'data-label': 'Status' }, hookStatus(w)),
    h('td', { 'data-label': 'Última entrega' }, w.lastSuccessAt ? formatDateTime(w.lastSuccessAt) : h('span', { class: 'muted' }, '—')),
    h('td', { class: 'cell-actions' }, h('span', { class: 'row-actions' },
      linkBtn('Enviar teste', () => sendTest(w)),
      linkBtn('Segredo', () => openSecret(w)),
      linkBtn('Editar', () => openHookForm(w)),
      linkBtn('Excluir', () => deleteHook(w), true)))));

  clear(hooksEl).append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Endereço'), h('th', {}, 'Eventos'), h('th', {}, 'Status'), h('th', {}, 'Última entrega'),
      h('th', {}, h('span', { class: 'visually-hidden' }, 'Ações')))),
    h('tbody', {}, rows)));
}

function openHookForm(existing) {
  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  const all = overview.eventTypes;
  const selected = new Set(existing?.events ?? []);
  const allMode = !existing || existing.events.length === 0;

  const allBox = h('input', { type: 'checkbox', checked: allMode });
  const boxes = all.map((t) => ({
    type: t.type,
    input: h('input', { type: 'checkbox', name: 'events', value: t.type, checked: allMode || selected.has(t.type), disabled: allMode }),
    description: t.description,
  }));
  allBox.addEventListener('change', () => {
    for (const b of boxes) { b.input.disabled = allBox.checked; if (allBox.checked) b.input.checked = true; }
  });

  const form = h('form', { class: 'form', novalidate: true, id: 'hook-form' },
    alertEl,
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'hook-url' }, 'URL'),
      h('input', { class: 'input', id: 'hook-url', name: 'url', type: 'url', inputmode: 'url', maxlength: 500, required: true, placeholder: 'https://seu-sistema.com.br/webhooks/contratos', value: existing?.url ?? '' }),
      h('p', { class: 'field__hint' }, 'Endereço público com https://. O seu sistema deve responder 2xx em até 10 segundos.'),
      h('p', { class: 'field__error', 'data-error-for': 'url' })),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 'hook-description' }, 'Descrição ', h('span', { class: 'muted' }, '(opcional)')),
      h('input', { class: 'input', id: 'hook-description', name: 'description', type: 'text', maxlength: 200, value: existing?.description ?? '' }),
      h('p', { class: 'field__error', 'data-error-for': 'description' })),
    h('fieldset', { class: 'field fieldset' },
      h('legend', { class: 'field__label' }, 'Eventos'),
      h('label', { class: 'checkbox' }, allBox, h('span', {}, h('strong', {}, 'Todos os eventos'), h('br'), h('small', { class: 'muted' }, 'Inclui os que forem criados no futuro.'))),
      h('div', { class: 'event-list' }, boxes.map((b) => h('label', { class: 'checkbox' }, b.input,
        h('span', {}, h('code', {}, b.type), h('br'), h('small', { class: 'muted' }, b.description))))),
      h('p', { class: 'field__error', 'data-error-for': 'events' })),
    existing ? h('label', { class: 'checkbox' },
      h('input', { type: 'checkbox', name: 'enabled', checked: existing.enabled }),
      h('span', {}, 'Ativo (desativado, não recebe eventos)')) : null);

  const submit = h('button', { type: 'submit', class: 'btn btn--primary', form: 'hook-form' }, existing ? 'Salvar' : 'Cadastrar');
  const cancel = h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => modal.close() }, 'Cancelar');
  const modal = openModal({
    title: existing ? 'Editar webhook' : 'Novo webhook',
    description: existing ? null : 'Cada evento chega como um POST em JSON, assinado com o segredo deste webhook.',
    body: form,
    actions: [cancel, submit],
  });
  form.elements.url.focus();

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    alertEl.hidden = true;
    const events = allBox.checked ? [] : boxes.filter((b) => b.input.checked).map((b) => b.type);
    if (!allBox.checked && events.length === 0) {
      showAlert(alertEl, 'Escolha ao menos um evento, ou marque “Todos os eventos”.');
      return;
    }
    const body = {
      url: form.elements.url.value.trim(),
      description: form.elements.description.value.trim() || null,
      events,
      enabled: existing ? form.elements.enabled.checked : null,
    };
    setBusy(submit, true);
    try {
      if (existing) {
        await api(`/integrations/webhooks/${existing.id}`, { method: 'PUT', body });
        modal.close();
        toast('Webhook salvo.');
      } else {
        const created = await api('/integrations/webhooks', { method: 'POST', body });
        form.replaceChildren(
          h('div', { class: 'alert alert--success' }, 'Webhook cadastrado. Configure o segredo abaixo no seu sistema para conferir a assinatura de cada evento.'),
          secretField('new-secret', 'Segredo de assinatura', created.secret, 'Você também pode ver o segredo depois, em “Segredo”.'),
          h('p', { class: 'muted' }, 'Como conferir a assinatura: ', h('a', { href: '/desenvolvedores/#assinatura', target: '_blank', rel: 'noopener' }, 'veja a documentação'), '.'));
        submit.remove();
        cancel.textContent = 'Concluir';
        cancel.classList.replace('btn--secondary', 'btn--primary');
      }
      await load();
    } catch (err) {
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    } finally {
      setBusy(submit, false);
    }
  });
}

async function openSecret(hook) {
  const body = h('div', { class: 'form' }, h('div', { class: 'loading' }, h('div', { class: 'spinner', 'aria-label': 'Carregando' })));
  const rotate = h('button', { type: 'button', class: 'btn btn--secondary' }, 'Gerar novo segredo');
  const modal = openModal({
    title: 'Segredo de assinatura',
    description: hook.url,
    body,
    actions: [rotate, h('button', { type: 'button', class: 'btn btn--primary', onclick: () => modal.close() }, 'Fechar')],
  });
  const show = (secret, rotated = false) => body.replaceChildren(
    rotated ? h('div', { class: 'alert alert--warning' }, 'Segredo novo. O anterior parou de valer: atualize o seu sistema.') : null,
    secretField('hook-secret', 'Segredo', secret, 'Use para conferir o cabeçalho X-Contratos-Signature de cada evento.'));
  try {
    show((await api(`/integrations/webhooks/${hook.id}/secret`)).secret);
  } catch (err) {
    body.replaceChildren(h('div', { class: 'alert alert--error' }, err.message));
  }
  rotate.addEventListener('click', async () => {
    const ok = await confirmModal({
      title: 'Gerar um novo segredo?',
      description: 'O segredo atual para de valer na hora: até você atualizar o seu sistema, ele vai recusar os eventos.',
      confirmLabel: 'Gerar novo', danger: true,
    });
    if (!ok) return;
    setBusy(rotate, true);
    try {
      show((await api(`/integrations/webhooks/${hook.id}/secret`, { method: 'POST' })).secret, true);
    } catch (err) { toast(err.message, 'error'); }
    finally { setBusy(rotate, false); }
  });
}

async function sendTest(hook) {
  try {
    await api(`/integrations/webhooks/${hook.id}/test`, { method: 'POST' });
    toast('Evento de teste na fila. O resultado aparece em Entregas recentes.');
    filterEl.value = hook.id;
    page = 1;
    // O worker entrega em poucos segundos: atualiza a lista duas vezes.
    setTimeout(loadDeliveries, 2500);
    setTimeout(loadDeliveries, 7000);
    loadDeliveries();
  } catch (err) { toast(err.message, 'error'); }
}

async function deleteHook(hook) {
  const ok = await confirmModal({
    title: 'Excluir este webhook?',
    description: `${hook.url} para de receber eventos e o histórico de entregas dele é apagado.`,
    confirmLabel: 'Excluir', danger: true,
  });
  if (!ok) return;
  try {
    await api(`/integrations/webhooks/${hook.id}`, { method: 'DELETE' });
    toast('Webhook excluído.');
    if (filterEl.value === hook.id) filterEl.value = '';
    await load();
  } catch (err) { toast(err.message, 'error'); }
}

// ---------- Embed ----------

function renderOrigins() {
  const origins = overview.embedOrigins;
  if (origins.length === 0) {
    clear(originsEl).append(h('div', { class: 'panel__body' },
      h('p', { class: 'muted' }, 'Nenhum site ainda. Sem um site cadastrado, a API não cria sessões de embed.')));
    return;
  }
  clear(originsEl).append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Site'), h('th', {}, 'Cadastrado'), h('th', {}, h('span', { class: 'visually-hidden' }, 'Ações')))),
    h('tbody', {}, origins.map((o) => h('tr', {},
      h('td', { 'data-label': 'Site' }, h('code', { class: 'cell-main' }, o.origin)),
      h('td', { 'data-label': 'Cadastrado' },
        h('span', { class: 'cell-main' }, formatDateTime(o.createdAt)),
        o.createdByName ? h('span', { class: 'cell-sub' }, `por ${o.createdByName}`) : null),
      h('td', { class: 'cell-actions' }, linkBtn('Remover', () => removeOrigin(o), true)))))));
}

async function removeOrigin(origin) {
  const ok = await confirmModal({
    title: `Remover ${origin.origin}?`,
    description: 'O embed para de abrir neste site, inclusive nas sessões que já estão abertas.',
    confirmLabel: 'Remover', danger: true,
  });
  if (!ok) return;
  try {
    await api(`/integrations/embed-origins/${origin.id}`, { method: 'DELETE' });
    toast('Site removido.');
    await load();
  } catch (err) { toast(err.message, 'error'); }
}

originForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(originForm);
  const submit = originForm.querySelector('[data-add-origin]');
  setBusy(submit, true);
  try {
    await api('/integrations/embed-origins', { method: 'POST', body: { origin: originForm.elements.origin.value.trim() } });
    originForm.reset();
    toast('Site cadastrado.');
    await load();
  } catch (err) {
    if (!(err instanceof ApiError && applyErrors(originForm, err.errors))) toast(err.message, 'error');
  } finally {
    setBusy(submit, false);
    renderOriginForm();
  }
});

function renderOriginForm() {
  const blocked = !overview.available || overview.embedOrigins.length >= overview.maxEmbedOrigins;
  originForm.elements.origin.disabled = blocked;
  originForm.querySelector('[data-add-origin]').disabled = blocked;
}

// ---------- Entregas ----------

function deliveryStatus(d) {
  if (d.status === 'pending' && d.attempts > 0)
    return h('span', { class: 'badge badge--warning', title: d.nextAttemptAt ? `Próxima tentativa: ${formatDateTime(d.nextAttemptAt)}` : null },
      `Tentando de novo (${d.attempts})`);
  const [label, cls] = DELIVERY_STATUS[d.status] ?? [d.status, 'badge--neutral'];
  return h('span', { class: `badge ${cls}` }, label);
}

function openDelivery(d) {
  const row = (label, value) => value === null || value === undefined || value === ''
    ? null
    : h('div', { class: 'detail-row' }, h('dt', {}, label), h('dd', {}, value));
  openModal({
    title: d.eventType,
    description: d.endpointUrl,
    body: h('dl', { class: 'detail-list' },
      row('Evento', h('code', {}, d.eventId)),
      row('Criado', formatDateTime(d.createdAt)),
      row('Tentativas', String(d.attempts)),
      row('Última tentativa', d.lastAttemptAt ? formatDateTime(d.lastAttemptAt) : null),
      row('Próxima tentativa', d.nextAttemptAt && d.attempts > 0 ? formatDateTime(d.nextAttemptAt) : null),
      row('Resposta HTTP', d.lastStatusCode ? String(d.lastStatusCode) : null),
      row('Erro', d.lastError),
      row('Corpo da resposta', d.responseBody ? h('pre', { class: 'code-block' }, d.responseBody) : null)),
    actions: [h('button', { type: 'button', class: 'btn btn--primary', onclick: (e) => e.target.closest('dialog').close() }, 'Fechar')],
  });
}

async function redeliver(d) {
  try {
    await api(`/integrations/deliveries/${d.id}/redeliver`, { method: 'POST' });
    toast('Entrega na fila.');
    page = 1;
    setTimeout(loadDeliveries, 2500);
    loadDeliveries();
  } catch (err) { toast(err.message, 'error'); }
}

async function loadDeliveries() {
  const params = new URLSearchParams({ page: String(page) });
  if (filterEl.value) params.set('endpointId', filterEl.value);
  let result;
  try {
    result = await api(`/integrations/deliveries?${params}`);
  } catch (err) {
    clear(deliveriesEl).append(errorBox(err));
    return;
  }
  if (result.items.length === 0) {
    clear(deliveriesEl).append(h('div', { class: 'panel__body' },
      h('p', { class: 'muted' }, overview.endpoints.length === 0
        ? 'As entregas aparecem aqui depois que você cadastrar um webhook.'
        : 'Nenhuma entrega ainda. Use “Enviar teste” para conferir a conexão.')));
    pagerEl.hidden = true;
    return;
  }
  const rows = result.items.map((d) => h('tr', {},
    h('td', { 'data-label': 'Quando' }, formatDateTime(d.createdAt)),
    h('td', { 'data-label': 'Evento' },
      h('code', { class: 'cell-main' }, d.eventType),
      d.contractNumber ? h('a', { class: 'cell-sub', href: `/contract.html?id=${d.contractId}` }, `Contrato nº ${String(d.contractNumber).padStart(4, '0')}`) : null),
    h('td', { 'data-label': 'Webhook' }, h('span', { class: 'cell-url' }, d.endpointUrl)),
    h('td', { 'data-label': 'Status' }, deliveryStatus(d),
      d.lastStatusCode || d.lastError ? h('span', { class: 'cell-sub' }, d.lastStatusCode ? `HTTP ${d.lastStatusCode}` : d.lastError) : null),
    h('td', { class: 'cell-actions' }, h('span', { class: 'row-actions' },
      linkBtn('Detalhes', () => openDelivery(d)),
      d.status !== 'pending' ? linkBtn('Entregar de novo', () => redeliver(d)) : null))));

  clear(deliveriesEl).append(h('table', { class: 'table table--stack' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Quando'), h('th', {}, 'Evento'), h('th', {}, 'Webhook'), h('th', {}, 'Status'),
      h('th', {}, h('span', { class: 'visually-hidden' }, 'Ações')))),
    h('tbody', {}, rows)));

  const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
  pagerEl.hidden = pages <= 1;
  pagerEl.querySelector('[data-prev]').disabled = page <= 1;
  pagerEl.querySelector('[data-next]').disabled = page >= pages;
  pagerEl.querySelector('[data-page-info]').textContent = `Página ${page} de ${pages}`;
}

// ---------- Carga ----------

function renderFilter() {
  const current = filterEl.value;
  filterEl.replaceChildren(h('option', { value: '' }, 'Todos os webhooks'),
    ...overview.endpoints.map((w) => h('option', { value: w.id, selected: w.id === current }, w.url)));
}

async function load() {
  try {
    overview = await api('/integrations');
  } catch (err) {
    clear(keysEl).append(errorBox(err));
    clear(hooksEl);
    clear(deliveriesEl);
    clear(originsEl);
    return;
  }
  const upgrade = document.querySelector('[data-upgrade]');
  upgrade.hidden = overview.available;
  upgrade.querySelector('[data-api-plans]').textContent = overview.plansWithApi.join(' e ');
  const activeKeys = overview.keys.filter((k) => !k.revokedAt).length;
  newKeyBtn.disabled = !overview.available || activeKeys >= overview.maxKeys;
  newHookBtn.disabled = !overview.available || overview.endpoints.length >= overview.maxEndpoints;
  renderKeys();
  renderHooks();
  renderOrigins();
  renderOriginForm();
  renderFilter();
  await loadDeliveries();
}

newKeyBtn.addEventListener('click', openNewKey);
newHookBtn.addEventListener('click', () => openHookForm(null));
filterEl.addEventListener('change', () => { page = 1; loadDeliveries(); });
document.querySelector('[data-refresh]').addEventListener('click', loadDeliveries);
pagerEl.querySelector('[data-prev]').addEventListener('click', () => { page = Math.max(1, page - 1); loadDeliveries(); });
pagerEl.querySelector('[data-next]').addEventListener('click', () => { page += 1; loadDeliveries(); });

main.hidden = false;
await load();
