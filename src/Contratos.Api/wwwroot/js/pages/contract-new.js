import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { clear, h } from '../ui/dom.js';
import { applyErrors, clearErrors, setBusy, showAlert } from '../ui/form.js';
import { confirmModal, openModal } from '../ui/modal.js';
import { toast } from '../ui/toast.js';
import { contractNumber, formatDate } from '../utils/format.js';
import { ACTIONS, DELIVERY } from '../utils/status.js';
import { cheapestWith, renewsLabel } from '../utils/billing.js';

await requireUser({ roles: ['Admin', 'Editor'] });

const params = new URLSearchParams(location.search);
let contractId = params.get('id');
let step = Number(params.get('step')) || (contractId ? 2 : 1);
let contract = null;
let selectedTemplate = null;
const sendKey = crypto.randomUUID(); // Idempotency-Key: duplo clique não envia duas vezes
/** Plano e cota; sem ele a tela funciona igual e o servidor decide no envio. */
let billing = null;
const billingReady = api('/organization/billing').then((b) => { billing = b; }).catch(() => {});

const alertEl = document.querySelector('[data-alert]');
const valuesForm = document.querySelector('[data-values]');
const reviewForm = document.getElementById('review-form');

// ---------- Navegação entre etapas ----------

function show(n) {
  step = n;
  for (const el of document.querySelectorAll('[data-step]')) el.hidden = Number(el.dataset.step) !== n;
  for (const li of document.querySelectorAll('[data-step-label]')) {
    const i = Number(li.dataset.stepLabel);
    li.classList.toggle('is-current', i === n);
    li.classList.toggle('is-done', i < n);
    li.toggleAttribute('aria-current', i === n);
  }
  if (contractId) history.replaceState(null, '', `?id=${contractId}&step=${n}`);
  hideError();
  if (n === 2) renderValues();
  if (n === 3) renderSigners();
  if (n === 4) renderReview();
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function showError(err, extra = []) {
  const items = [...extra];
  if (err instanceof ApiError) {
    for (const [key, msgs] of Object.entries(err.errors ?? {})) {
      if (!document.querySelector(`[data-step="${step}"] [data-error-for="${key}"]`)) items.push(msgs[0]);
    }
  }
  alertEl.replaceChildren(h('div', { class: 'alert__body' },
    h('strong', {}, err.message || 'Algo deu errado.'),
    items.length ? h('ul', { class: 'error-list' }, items.map((m) => h('li', {}, m))) : null));
  alertEl.hidden = false;
  alertEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
}
const hideError = () => { alertEl.hidden = true; };

function setHeader() {
  if (!contract) return;
  document.querySelector('[data-title]').textContent = contract.title;
  document.querySelector('[data-subtitle]').textContent = `Contrato nº ${contractNumber(contract.number)} · rascunho · ${contract.template.name} v${contract.template.version}`;
  document.title = `${contract.title} · Contratos`;
}

/** PUT do rascunho: manda sempre o estado completo, com o que mudou por cima. */
async function saveDraft(patch = {}) {
  const body = {
    title: contract.title,
    variableValues: contract.variableValues,
    expiresAt: contract.expiresAt ? contract.expiresAt.slice(0, 10) : null,
    signingMode: contract.signingMode,
    message: contract.message,
    ...patch,
  };
  contract = await api(`/contracts/${contractId}`, { method: 'PUT', body });
  setHeader();
  return contract;
}

// ---------- 1. Template ----------

async function renderTemplates() {
  const grid = document.querySelector('[data-templates]');
  const templates = (await api('/templates')).filter((t) => t.currentVersion);
  if (templates.length === 0) {
    grid.replaceChildren(h('div', { class: 'empty' },
      h('h2', {}, 'Nenhum template publicado'),
      h('p', {}, 'Um Admin precisa criar e publicar um modelo de contrato antes.'),
      h('a', { class: 'btn btn--secondary', href: '/templates.html' }, 'Ir para templates')));
    return;
  }
  const titleInput = document.getElementById('title');
  const createBtn = document.querySelector('[data-create]');
  grid.replaceChildren(...templates.map((t) => {
    const card = h('button', { type: 'button', class: 'template-card', role: 'radio', 'aria-checked': 'false' },
      h('strong', {}, t.name),
      h('span', {}, t.description || `${t.variableCount} variáveis`),
      h('span', {}, `Versão ${t.currentVersion}`));
    card.addEventListener('click', () => {
      for (const c of grid.children) c.setAttribute('aria-checked', String(c === card));
      if (!titleInput.value || titleInput.value === selectedTemplate?.name) titleInput.value = t.name;
      selectedTemplate = t;
      createBtn.disabled = false;
    });
    return card;
  }));
}

document.querySelector('[data-create]').addEventListener('click', async (e) => {
  const btn = e.currentTarget;
  setBusy(btn, true);
  try {
    contract = await api('/contracts', { method: 'POST', body: { templateId: selectedTemplate.id, title: document.getElementById('title').value.trim() || null } });
    contractId = contract.id;
    setHeader();
    show(2);
  } catch (err) {
    showError(err);
  } finally {
    setBusy(btn, false);
  }
});

// ---------- 2. Dados ----------

const TYPE_HINT = { CpfCnpj: 'Só números ou com pontuação.', Money: 'Ex.: 1.500,00', Number: 'Ex.: 12 ou 12,5' };

function valueInput(v) {
  const id = `var-${v.key}`;
  const value = contract.variableValues[v.key] ?? '';
  const common = { class: 'input', id, name: v.key, 'aria-describedby': `${id}-error` };
  switch (v.type) {
    case 'Date': return h('input', { ...common, type: 'date', value });
    case 'Money': return h('div', { class: 'input-prefix' }, h('span', { 'aria-hidden': 'true' }, 'R$'), h('input', { ...common, inputmode: 'decimal', value }));
    case 'Number': return h('input', { ...common, inputmode: 'decimal', value });
    case 'CpfCnpj': return h('input', { ...common, inputmode: 'numeric', value, maxlength: 18 });
    default: return h('input', { ...common, value, maxlength: 2000 });
  }
}

function renderValues() {
  const vars = contract.template.variables;
  if (vars.length === 0) {
    valuesForm.replaceChildren(h('p', { class: 'muted span-2' }, 'Este template não tem variáveis. Siga para os signatários.'));
    return;
  }
  valuesForm.replaceChildren(...vars.map((v) => h('div', { class: 'field' },
    h('label', { class: 'field__label', for: `var-${v.key}` }, v.label, v.required ? h('span', { class: 'required', 'aria-hidden': 'true' }, '*') : null),
    valueInput(v),
    TYPE_HINT[v.type] ? h('p', { class: 'field__hint' }, TYPE_HINT[v.type]) : null,
    h('p', { class: 'field__error', id: `var-${v.key}-error`, 'data-error-for': `variableValues.${v.key}` }))));
}

function readValues() {
  const values = {};
  for (const v of contract.template.variables) {
    const el = valuesForm.elements.namedItem(v.key);
    if (el?.value.trim()) values[v.key] = el.value.trim();
  }
  return values;
}

function applyValueErrors(err) {
  let first = null;
  for (const [key, msgs] of Object.entries(err.errors ?? {})) {
    const slot = valuesForm.querySelector(`[data-error-for="${key}"]`);
    if (!slot) continue;
    slot.textContent = msgs[0];
    const input = valuesForm.elements.namedItem(key.replace(/^variableValues\./, ''));
    input?.setAttribute('aria-invalid', 'true');
    first ??= input;
  }
  first?.focus();
  return first !== null;
}

document.querySelector('[data-save-values]').addEventListener('click', async (e) => {
  const btn = e.currentTarget;
  clearErrors(valuesForm);
  hideError();
  setBusy(btn, true);
  try {
    await saveDraft({ variableValues: readValues() });
    show(3);
  } catch (err) {
    if (!(err instanceof ApiError && applyValueErrors(err))) showError(err);
  } finally {
    setBusy(btn, false);
  }
});

// ---------- 3. Signatários ----------

function contactOf(s) {
  return s.deliveryMethod === 'Email' ? s.email : s.phone;
}

function renderSigners() {
  const list = document.querySelector('[data-signers]');
  const sequential = contract.signingMode === 'Sequential';
  for (const r of document.querySelectorAll('input[name="signingMode"]')) r.checked = r.value === contract.signingMode;

  const suggestions = document.querySelector('[data-suggestions]');
  const used = new Set(contract.signers.map((s) => s.role));
  const missing = contract.template.suggestedRoles.filter((r) => !used.has(r.role));
  suggestions.hidden = missing.length === 0;
  suggestions.replaceChildren(h('span', { class: 'muted' }, 'Papéis do template:'),
    ...missing.map((r) => h('button', { type: 'button', class: 'chip', onclick: () => openSigner(null, r) }, `+ ${r.role}`)));

  clear(list).append(...contract.signers.map((s, i) => h('li', { class: 'signer-card' },
    h('span', { class: 'signer-card__order', title: sequential ? 'Ordem de assinatura' : null }, sequential ? String(i + 1) : '•'),
    h('span', { class: 'signer-card__info' },
      h('strong', {}, s.name),
      h('span', {}, [s.role, ACTIONS[s.action] !== 'Assinar' ? ACTIONS[s.action] : null].filter(Boolean).join(' · ') || 'Signatário'),
      h('span', {}, `${DELIVERY[s.deliveryMethod]}: ${contactOf(s)}`)),
    h('span', { class: 'signer-card__actions' },
      sequential ? h('button', { type: 'button', class: 'icon-btn', 'aria-label': `Subir ${s.name}`, disabled: i === 0, onclick: () => move(i, -1) }, '↑') : null,
      sequential ? h('button', { type: 'button', class: 'icon-btn', 'aria-label': `Descer ${s.name}`, disabled: i === contract.signers.length - 1, onclick: () => move(i, 1) }, '↓') : null,
      h('button', { type: 'button', class: 'icon-btn', onclick: () => openSigner(s) }, 'Editar'),
      h('button', { type: 'button', class: 'icon-btn icon-btn--danger', 'aria-label': `Remover ${s.name}`, onclick: () => removeSigner(s) }, '×')))));
  document.querySelector('[data-signers-empty]').hidden = contract.signers.length > 0;
}

function signerBody(s) {
  return {
    name: s.name, email: s.email, phone: s.phone, cpf: s.cpf, deliveryMethod: s.deliveryMethod,
    role: s.role, action: s.action, signOrder: s.signOrder,
  };
}

async function move(index, delta) {
  const a = contract.signers[index];
  const b = contract.signers[index + delta];
  try {
    // Troca as ordens; se forem iguais, abre espaço.
    const orderA = b.signOrder === a.signOrder ? a.signOrder + delta : b.signOrder;
    await api(`/contracts/${contractId}/signers/${a.id}`, { method: 'PUT', body: { ...signerBody(a), signOrder: orderA } });
    contract = await api(`/contracts/${contractId}/signers/${b.id}`, { method: 'PUT', body: { ...signerBody(b), signOrder: a.signOrder } });
    renderSigners();
  } catch (err) { showError(err); }
}

async function removeSigner(s) {
  const ok = await confirmModal({ title: `Remover ${s.name}?`, confirmLabel: 'Remover', danger: true });
  if (!ok) return;
  try {
    contract = await api(`/contracts/${contractId}/signers/${s.id}`, { method: 'DELETE' });
    renderSigners();
  } catch (err) { showError(err); }
}

function field(label, input, name, hint) {
  return h('div', { class: 'field' },
    h('label', { class: 'field__label', for: input.id ?? input.querySelector?.('input')?.id }, label),
    input,
    hint ? h('p', { class: 'field__hint' }, hint) : null,
    h('p', { class: 'field__error', 'data-error-for': name }));
}

function openSigner(existing, suggestion) {
  const s = existing ?? { name: '', email: '', phone: '', cpf: '', deliveryMethod: 'Email', role: suggestion?.role ?? '', action: suggestion?.action ?? 'Sign' };
  const alertBox = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });
  // Canal fora do plano fica desabilitado (a não ser que o signatário já use, para não sumir com o dado).
  const allowed = (value) => !billing || billing.plan.channels.includes(value) || value === existing?.deliveryMethod;
  const channel = h('div', { class: 'channel-tabs', role: 'radiogroup', 'aria-label': 'Canal de entrega' },
    Object.entries(DELIVERY).map(([value, label]) => h('label', { class: `radio-card${allowed(value) ? '' : ' is-disabled'}` },
      h('input', { type: 'radio', name: 'deliveryMethod', value, checked: value === s.deliveryMethod, disabled: !allowed(value) }),
      h('span', {}, h('strong', {}, label),
        allowed(value) ? null : h('small', { class: 'radio-card__hint' }, `No plano ${cheapestWith(billing.plans, value)?.name ?? 'superior'}`)))));

  const form = h('form', { class: 'form', id: 'signer-form', novalidate: true },
    alertBox,
    field('Nome completo', h('input', { class: 'input', id: 'sg-name', name: 'name', value: s.name, maxlength: 100, autocomplete: 'off' }), 'name'),
    h('div', { class: 'form-grid form-grid--2' },
      field('Papel', h('input', { class: 'input', id: 'sg-role', name: 'role', value: s.role ?? '', maxlength: 60, placeholder: 'Contratante' }), 'role'),
      field('Ação', h('select', { class: 'select select--block', id: 'sg-action', name: 'action' },
        Object.entries({ Sign: 'Assinar', SignAsWitness: 'Assinar como testemunha', Approve: 'Aprovar', Recognize: 'Reconhecer' })
          .map(([v, l]) => h('option', { value: v, selected: v === s.action }, l))), 'action')),
    h('fieldset', { class: 'fieldset field' }, h('legend', { class: 'field__label' }, 'Como o link chega'), channel, h('p', { class: 'field__error', 'data-error-for': 'deliveryMethod' })),
    field('E-mail', h('input', { class: 'input', id: 'sg-email', name: 'email', type: 'email', value: s.email ?? '', autocomplete: 'off' }), 'email'),
    field('Celular', h('input', { class: 'input', id: 'sg-phone', name: 'phone', type: 'tel', value: s.phone ?? '', placeholder: '+55 11 98765-4321' }), 'phone', 'Obrigatório para WhatsApp e SMS. Sem código do país, consideramos Brasil (+55).'),
    field('CPF (opcional)', h('input', { class: 'input', id: 'sg-cpf', name: 'cpf', inputmode: 'numeric', value: s.cpf ?? '', maxlength: 14 }), 'cpf', 'Se preenchido, só este CPF consegue assinar.'));

  const syncRequired = () => {
    const m = form.elements.deliveryMethod.value;
    form.querySelector('label[for="sg-email"]').textContent = m === 'Email' ? 'E-mail' : 'E-mail (opcional)';
    form.querySelector('label[for="sg-phone"]').textContent = m === 'Email' ? 'Celular (opcional)' : 'Celular';
  };
  form.addEventListener('change', syncRequired);
  syncRequired();

  const submit = h('button', { type: 'submit', class: 'btn btn--primary', form: 'signer-form' }, existing ? 'Salvar' : 'Adicionar');
  const { close } = openModal({
    title: existing ? `Editar ${existing.name}` : 'Adicionar signatário',
    body: form,
    actions: [h('button', { type: 'button', class: 'btn btn--secondary', onclick: () => close() }, 'Cancelar'), submit],
  });
  form.elements.name.focus();

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    alertBox.hidden = true;
    const body = {
      name: form.elements.name.value.trim(),
      role: form.elements.role.value.trim() || null,
      action: form.elements.action.value,
      deliveryMethod: form.elements.deliveryMethod.value,
      email: form.elements.email.value.trim() || null,
      phone: form.elements.phone.value.trim() || null,
      cpf: form.elements.cpf.value.trim() || null,
      signOrder: existing?.signOrder,
    };
    setBusy(submit, true);
    try {
      contract = existing
        ? await api(`/contracts/${contractId}/signers/${existing.id}`, { method: 'PUT', body })
        : await api(`/contracts/${contractId}/signers`, { method: 'POST', body });
      close();
      renderSigners();
    } catch (err) {
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertBox, err.message);
    } finally {
      setBusy(submit, false);
    }
  });
}

for (const radio of document.querySelectorAll('input[name="signingMode"]')) {
  radio.addEventListener('change', async () => {
    try { await saveDraft({ signingMode: radio.value }); renderSigners(); }
    catch (err) { showError(err); }
  });
}

document.querySelector('[data-add-signer]').addEventListener('click', () => openSigner(null));
document.querySelector('[data-to-review]').addEventListener('click', (e) => {
  if (contract.signers.length === 0) {
    e.stopImmediatePropagation();
    showError(new Error('Adicione ao menos um signatário.'));
  }
}, true);

// ---------- 4. Revisar ----------

function displayValue(v, raw) {
  if (!raw) return null;
  if (v.type === 'Date') return formatDate(`${raw}T12:00:00`);
  if (v.type === 'Money') return `R$ ${raw}`;
  return raw;
}

function renderReview() {
  reviewForm.elements.title.value = contract.title;
  reviewForm.elements.expiresAt.value = contract.expiresAt ? contract.expiresAt.slice(0, 10) : '';
  reviewForm.elements.expiresAt.min = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
  reviewForm.elements.message.value = contract.message ?? '';
  document.querySelector('[data-review-template]').textContent = `${contract.template.name} · v${contract.template.version}`;
  document.querySelector('[data-preview-link]').href = `/api/v1/contracts/${contractId}/preview`;

  const values = document.querySelector('[data-review-values]');
  clear(values);
  for (const v of contract.template.variables) {
    const shown = displayValue(v, contract.variableValues[v.key]);
    values.append(h('dt', {}, v.label), h('dd', { class: shown ? null : 'is-missing' }, shown ?? (v.required ? 'Faltando' : '—')));
  }
  if (contract.template.variables.length === 0) values.append(h('dd', { class: 'muted' }, 'Sem variáveis.'));

  renderUsageNote();
  document.querySelector('[data-review-mode]').textContent = contract.signingMode === 'Sequential' ? '(em ordem)' : '(todos ao mesmo tempo)';
  document.querySelector('[data-review-signers]').replaceChildren(...contract.signers.map((s) =>
    h('li', {}, h('strong', {}, s.name), ' ', h('span', {}, `${s.role ? `${s.role} · ` : ''}${DELIVERY[s.deliveryMethod]}: ${contactOf(s)}`))));
}

/** De onde sai este envio: cota do plano, crédito avulso ou nenhum dos dois. */
function renderUsageNote() {
  const el = document.querySelector('[data-usage-note]');
  if (!billing) { el.hidden = true; return; }
  const left = Math.max(0, billing.contractsLimit - billing.contractsUsed);
  const settings = h('a', { href: '/settings.html' }, 'Ver plano e uso');
  let tone = 'alert--info';
  let text;
  if (left > 0) {
    text = `Este envio usa 1 dos ${left} ${left === 1 ? 'contrato restante' : 'contratos restantes'} do plano ${billing.plan.name} neste ciclo.`;
    if (left > 1 && billing.contractsUsed + 1 < billing.contractsLimit * 0.8) { el.hidden = true; return; }
  } else if (billing.creditsAvailable > 0) {
    text = `Os contratos do plano acabaram neste ciclo. Este envio usa 1 crédito avulso (você tem ${billing.creditsAvailable}).`;
  } else {
    tone = 'alert--error';
    text = `Os contratos do plano ${billing.plan.name} acabaram neste ciclo (renova em ${renewsLabel(billing.periodEnd)}) e não há créditos avulsos. Compre um pacote ou mude de plano para enviar.`;
    if (billing.onlineCheckout) {
      settings.textContent = 'Comprar pacote de contratos';
      settings.href = '/settings.html#pacotes';
    }
  }
  el.className = `alert usage-note ${tone}`;
  el.replaceChildren(h('div', { class: 'alert__body' }, h('span', {}, text), settings));
  el.hidden = false;
}

document.querySelector('[data-send]').addEventListener('click', async (e) => {
  const btn = e.currentTarget;
  clearErrors(reviewForm);
  hideError();
  const ok = await confirmModal({
    title: 'Enviar para assinatura?',
    description: `${contract.signers.length} ${contract.signers.length === 1 ? 'pessoa vai receber' : 'pessoas vão receber'} o link. Depois do envio o contrato não pode mais ser editado.`,
    confirmLabel: 'Enviar',
  });
  if (!ok) return;

  setBusy(btn, true);
  try {
    await saveDraft({
      title: reviewForm.elements.title.value.trim(),
      expiresAt: reviewForm.elements.expiresAt.value || null,
      message: reviewForm.elements.message.value.trim() || null,
    });
    await sendWithKey();
    toast('Contrato enviado. Os links estão a caminho.');
    location.assign(`/contract.html?id=${contractId}`);
  } catch (err) {
    setBusy(btn, false);
    if (err instanceof ApiError && applyErrors(reviewForm, err.errors)) return;
    const missing = err instanceof ApiError
      ? Object.keys(err.errors ?? {}).filter((k) => k.startsWith('variableValues.')).length
      : 0;
    showError(err, missing ? [`Há ${missing} ${missing === 1 ? 'campo' : 'campos'} para completar na etapa Dados.`] : []);
  }
});

async function sendWithKey() {
  const csrf = (await api('/auth/csrf')).token;
  const res = await fetch(`/api/v1/contracts/${contractId}/send`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', 'X-CSRF-TOKEN': csrf, 'Idempotency-Key': sendKey },
  });
  if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => null));
}

for (const btn of document.querySelectorAll('[data-goto]')) {
  btn.addEventListener('click', () => show(Number(btn.dataset.goto)));
}

// ---------- Carga ----------

await billingReady;
if (contractId) {
  try {
    contract = await api(`/contracts/${contractId}`);
  } catch (err) {
    document.querySelector('[data-main]').hidden = false;
    showError(err);
    throw err;
  }
  if (contract.status !== 'Draft') location.replace(`/contract.html?id=${contractId}`);
  setHeader();
  show(Math.min(Math.max(step, 2), 4));
} else {
  await renderTemplates();
  show(1);
}
document.querySelector('[data-main]').hidden = false;
