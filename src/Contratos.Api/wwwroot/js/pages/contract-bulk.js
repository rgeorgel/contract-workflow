import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { h } from '../ui/dom.js';
import { setBusy } from '../ui/form.js';
import { toast } from '../ui/toast.js';
import { buildCsv, download, parseCsv, readCsvFile } from '../utils/csv.js';
import { contractNumber } from '../utils/format.js';
import { DELIVERY } from '../utils/status.js';

await requireUser({ roles: ['Admin', 'Editor'] });

const MAX_ROWS = 100;
const SIGNER_FIELDS = ['nome', 'email', 'celular', 'canal', 'papel', 'cpf'];
const TYPE_HELP = {
  Text: 'Texto livre.',
  Number: 'Número, ex.: 12 ou 3,5.',
  Money: 'Valor em reais, ex.: 1.500,00.',
  Date: 'Data no formato dd/mm/aaaa, ex.: 15/03/2027.',
  CpfCnpj: 'CPF ou CNPJ, com ou sem pontuação.',
};
const CHANNELS = { email: 'Email', 'e-mail': 'Email', whatsapp: 'WhatsApp', zap: 'WhatsApp', sms: 'Sms' };

const select = document.querySelector('[data-template]');
const downloadBtn = document.querySelector('[data-download]');
const fileInput = document.querySelector('[data-file]');
const fileAlert = document.querySelector('[data-file-alert]');
const previewPanel = document.querySelector('[data-preview-panel]');
const rowsEl = document.querySelector('[data-rows]');
const createBtn = document.querySelector('[data-create]');
const createSendBtn = document.querySelector('[data-create-send]');
const progressEl = document.querySelector('[data-progress]');

let template = null; // versão publicada: { name, variables, defaultSignerRoles }
let parsed = [];     // [{ line, row, clientErrors }]
let billing = null;

// ---------- 1. Template ----------

async function loadTemplates() {
  const templates = (await api('/templates')).filter((t) => t.currentVersion);
  if (templates.length === 0) {
    select.replaceChildren(h('option', { value: '' }, 'Nenhum template publicado'));
    select.disabled = true;
    return;
  }
  select.replaceChildren(h('option', { value: '' }, 'Escolha o template…'), ...templates.map((t) => h('option', { value: t.id }, t.name)));
  const preselect = new URLSearchParams(location.search).get('template');
  if (preselect && templates.some((t) => t.id === preselect)) { select.value = preselect; await chooseTemplate(); }
}

select.addEventListener('change', chooseTemplate);

async function chooseTemplate() {
  resetPreview();
  fileInput.value = '';
  template = null;
  downloadBtn.disabled = fileInput.disabled = true;
  document.querySelector('[data-help]').hidden = true;
  if (!select.value) return;
  const detail = await api(`/templates/${select.value}`);
  // A versão publicada também tem "id" (o da versão): o do template vem por último para valer.
  template = { ...detail.current, id: detail.id, name: detail.name };
  downloadBtn.disabled = fileInput.disabled = false;
  renderHelp();
}

/** Quantos blocos de signatário a planilha modelo traz: um por papel sugerido no template (no mínimo 1). */
const signerSlots = () => Math.max(1, template.defaultSignerRoles.length);

function headers() {
  const cols = ['titulo', ...template.variables.map((v) => v.key), 'prazo', 'mensagem', 'em_ordem'];
  for (let i = 1; i <= signerSlots(); i++) cols.push(...SIGNER_FIELDS.map((f) => `signatario${i}_${f}`));
  return cols;
}

function renderHelp() {
  const row = (col, text) => h('tr', {}, h('td', {}, h('code', {}, col)), h('td', {}, text));
  const roles = template.defaultSignerRoles;
  document.querySelector('[data-columns]').replaceChildren(
    row('titulo', 'Título do contrato (obrigatório).'),
    ...template.variables.map((v) => row(v.key, `${v.label}${v.required ? ' (obrigatório)' : ' (opcional)'}. ${TYPE_HELP[v.type] ?? ''}`)),
    row('prazo', 'Opcional. Último dia para assinar, dd/mm/aaaa.'),
    row('mensagem', 'Opcional. Mensagem para quem assina; vazio usa a padrão da empresa.'),
    row('em_ordem', 'Opcional. "sim" para assinarem um depois do outro, na ordem dos signatários.'),
    row('signatarioN_nome', `Nome de quem assina. N vai de 1 a ${signerSlots()} no modelo; dá para acrescentar colunas para mais gente.`),
    row('signatarioN_email', 'E-mail (obrigatório quando o canal é e-mail).'),
    row('signatarioN_celular', 'Celular com DDD (obrigatório para WhatsApp e SMS).'),
    row('signatarioN_canal', 'email, whatsapp ou sms. Vazio: email.'),
    row('signatarioN_papel', roles.length
      ? `Opcional. Vazio usa o papel do template: ${roles.map((r, i) => `${i + 1} = ${r.role}`).join(', ')}.`
      : 'Opcional, ex.: Contratante, Testemunha.'),
    row('signatarioN_cpf', 'Opcional. Se preenchido, só este CPF consegue assinar.'));
  document.querySelector('[data-help]').hidden = false;
}

downloadBtn.addEventListener('click', () => {
  const slug = template.name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  download(`lote-${slug || 'contratos'}.csv`, buildCsv([headers()]));
});

// ---------- 2. Planilha ----------

const norm = (s) => s.trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

/** dd/mm/aaaa (ou aaaa-mm-dd) para aaaa-mm-dd; null se não for uma data. */
function toIsoDate(value) {
  const v = value.trim();
  let m = v.match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/);
  if (m) m = [m[3], m[2], m[1]];
  else m = v.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/)?.slice(1);
  if (!m) return null;
  const [y, mo, d] = m.map(Number);
  const date = new Date(Date.UTC(y, mo - 1, d));
  if (date.getUTCFullYear() !== y || date.getUTCMonth() !== mo - 1 || date.getUTCDate() !== d) return null;
  return `${y}-${String(mo).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

/** Uma linha da planilha no formato da API, mais os erros que dá para ver antes de enviar. */
function toRow(header, cells) {
  const get = (col) => (cells[header.indexOf(col)] ?? '').trim();
  const clientErrors = {};
  const values = {};
  for (const v of template.variables) {
    const raw = get(v.key);
    if (!raw) continue;
    if (v.type === 'Date') {
      const iso = toIsoDate(raw);
      if (iso) values[v.key] = iso;
      else clientErrors[v.key] = ['Data inválida: use dd/mm/aaaa.'];
    } else values[v.key] = raw;
  }

  let expiresAt = null;
  if (get('prazo')) {
    expiresAt = toIsoDate(get('prazo'));
    if (!expiresAt) clientErrors.prazo = ['Data inválida: use dd/mm/aaaa.'];
  }

  const slots = [...new Set(header.map((c) => c.match(/^signatario(\d+)_/)?.[1]).filter(Boolean).map(Number))].sort((a, b) => a - b);
  const signers = [];
  for (const n of slots) {
    const f = (field) => get(`signatario${n}_${field}`);
    if (SIGNER_FIELDS.every((field) => !f(field))) continue;
    const channelRaw = f('canal');
    const deliveryMethod = channelRaw ? CHANNELS[norm(channelRaw)] : 'Email';
    if (!deliveryMethod) clientErrors[`signatario${n}_canal`] = ['Use email, whatsapp ou sms.'];
    const suggestion = template.defaultSignerRoles[n - 1];
    signers.push({
      slot: n,
      name: f('nome'),
      email: f('email') || null,
      phone: f('celular') || null,
      cpf: f('cpf') || null,
      deliveryMethod: deliveryMethod ?? 'Email',
      role: f('papel') || suggestion?.role || null,
      action: suggestion && (!f('papel') || norm(f('papel')) === norm(suggestion.role)) ? suggestion.action : 'Sign',
    });
  }

  const inOrder = ['sim', 's', 'x', 'true', '1'].includes(norm(get('em_ordem')));
  return {
    title: get('titulo'),
    row: {
      title: get('titulo'),
      variableValues: values,
      expiresAt,
      message: get('mensagem') || null,
      signingMode: inOrder ? 'Sequential' : 'Parallel',
      signers: signers.map(({ slot, ...s }) => ({ ...s, signOrder: inOrder ? slot : null })),
    },
    slots: signers.map((s) => s.slot),
    signerNames: signers.map((s) => `${s.name || '?'} (${DELIVERY[s.deliveryMethod]})`),
    clientErrors,
  };
}

fileInput.addEventListener('change', async () => {
  resetPreview();
  fileAlert.hidden = true;
  const file = fileInput.files[0];
  if (!file || !template) return;
  try {
    const table = parseCsv(await readCsvFile(file));
    if (table.length < 2) throw new Error('A planilha está vazia: preencha uma linha por contrato abaixo do cabeçalho.');
    const header = table[0].map((c) => c.trim().toLowerCase());
    if (!header.includes('titulo')) throw new Error('Não achei a coluna "titulo". Use a planilha modelo deste template.');
    const missing = template.variables.filter((v) => v.required && !header.includes(v.key.toLowerCase())).map((v) => v.key);
    if (missing.length) throw new Error(`Faltam colunas do template: ${missing.join(', ')}. Baixe a planilha modelo de novo.`);
    if (table.length - 1 > MAX_ROWS) throw new Error(`A planilha tem ${table.length - 1} contratos; envie no máximo ${MAX_ROWS} por vez.`);
    const keyed = header.map((col) => template.variables.find((v) => v.key.toLowerCase() === col)?.key ?? col);
    parsed = table.slice(1).map((cells, i) => ({ line: i + 2, ...toRow(keyed, cells) }));
    await validate();
  } catch (err) {
    fileAlert.textContent = err.message;
    fileAlert.hidden = false;
  }
});

// ---------- 3. Conferir e enviar ----------

/** Nome da coluna da planilha para o campo do erro devolvido pela API. */
function columnOf(field, item) {
  const m = field.match(/^signers\[(\d+)\]\.(\w+)$/);
  if (m) {
    const col = { name: 'nome', email: 'email', phone: 'celular', deliveryMethod: 'canal', role: 'papel', cpf: 'cpf', signOrder: 'ordem' }[m[2]] ?? m[2];
    return `signatario${item.slots[Number(m[1])]}_${col}`;
  }
  if (field.startsWith('variableValues.')) return field.slice('variableValues.'.length);
  return { title: 'titulo', expiresAt: 'prazo', message: 'mensagem', signers: 'signatários', signingMode: 'em_ordem' }[field] ?? field;
}

function resetPreview() {
  parsed = [];
  previewPanel.hidden = true;
  progressEl.hidden = true;
  createBtn.disabled = createSendBtn.disabled = true;
}

async function validate() {
  const res = await api('/contracts/bulk', { method: 'POST', body: { templateId: template.id, rows: parsed.map((p) => p.row), dryRun: true } });
  res.rows.forEach((r, i) => { parsed[i].errors = mergeErrors(parsed[i], r.errors); });
  renderPreview();
}

function mergeErrors(item, serverErrors) {
  const out = { ...item.clientErrors };
  for (const [field, messages] of Object.entries(serverErrors ?? {})) {
    const col = columnOf(field, item);
    out[col] = [...(out[col] ?? []), ...messages];
  }
  return out;
}

function statusCell(item) {
  if (item.result) return item.result;
  const entries = Object.entries(item.errors ?? {});
  if (!entries.length) return h('span', { class: 'badge badge--success' }, 'Pronto');
  return h('ul', { class: 'error-list' }, entries.map(([col, msgs]) => h('li', {}, h('code', {}, col), ` ${msgs.join(' ')}`)));
}

function renderPreview() {
  rowsEl.replaceChildren(...parsed.map((item) => h('tr', { class: Object.keys(item.errors ?? {}).length ? 'has-error' : null },
    h('td', { 'data-label': 'Linha' }, String(item.line)),
    h('td', { 'data-label': 'Contrato' }, item.contractNumber
      ? h('a', { href: `/contract.html?id=${item.contractId}` }, `Nº ${contractNumber(item.contractNumber)} · ${item.title}`)
      : (item.title || h('span', { class: 'muted' }, 'sem título'))),
    h('td', { 'data-label': 'Signatários' }, item.signerNames.join(', ') || h('span', { class: 'muted' }, 'nenhum')),
    h('td', { 'data-label': 'Situação' }, statusCell(item)))));

  const bad = parsed.filter((p) => Object.keys(p.errors ?? {}).length).length;
  const summary = document.querySelector('[data-summary]');
  summary.className = `badge ${bad ? 'badge--danger' : 'badge--success'}`;
  summary.textContent = bad
    ? `${bad} de ${parsed.length} ${parsed.length === 1 ? 'linha' : 'linhas'} com erro`
    : `${parsed.length} ${parsed.length === 1 ? 'contrato pronto' : 'contratos prontos'}`;

  const quota = document.querySelector('[data-quota]');
  if (!bad && billing.availableSends < parsed.length) {
    quota.className = 'alert alert--info';
    quota.textContent = `Sua conta tem ${billing.availableSends} ${billing.availableSends === 1 ? 'envio disponível' : 'envios disponíveis'} agora. `
      + 'Com "Criar e enviar", os contratos além disso ficam como rascunho para enviar depois.';
    quota.hidden = false;
  } else quota.hidden = true;

  createBtn.textContent = `Criar ${parsed.length} ${parsed.length === 1 ? 'rascunho' : 'rascunhos'}`;
  createSendBtn.textContent = `Criar e enviar ${parsed.length}`;
  createBtn.disabled = createSendBtn.disabled = bad > 0;
  if (bad) {
    progressEl.textContent = 'Corrija as linhas com erro na planilha e envie o arquivo de novo.';
    progressEl.hidden = false;
  }
  previewPanel.hidden = false;
}

async function create(andSend) {
  const buttons = [createBtn, createSendBtn];
  buttons.forEach((b) => setBusy(b, true));
  fileInput.disabled = select.disabled = true;
  try {
    const res = await api('/contracts/bulk', { method: 'POST', body: { templateId: template.id, rows: parsed.map((p) => p.row), dryRun: false } });
    if (!res.created) {
      res.rows.forEach((r, i) => { parsed[i].errors = mergeErrors(parsed[i], r.errors); });
      renderPreview();
      return;
    }
    res.rows.forEach((r, i) => Object.assign(parsed[i], { contractId: r.contractId, contractNumber: r.number, result: h('span', { class: 'badge badge--neutral' }, 'Rascunho') }));
    renderPreview();
    document.querySelector('[data-actions]').hidden = true;
    if (andSend) await sendAll();
    else {
      progressEl.textContent = `${parsed.length} ${parsed.length === 1 ? 'rascunho criado' : 'rascunhos criados'}. Abra cada um para revisar e enviar.`;
      progressEl.hidden = false;
      toast('Rascunhos criados.');
    }
  } catch (err) {
    toast(err.message, 'error');
  } finally {
    buttons.forEach((b) => setBusy(b, false));
  }
}

/** Envia um por um (cada envio gera o PDF e usa a cota ou um crédito); para se a cota acabar. */
async function sendAll() {
  let sent = 0;
  let stopped = null;
  progressEl.hidden = false;
  for (const [i, item] of parsed.entries()) {
    if (stopped) {
      item.result = h('span', { class: 'badge badge--neutral' }, 'Ficou como rascunho');
      continue;
    }
    progressEl.textContent = `Enviando ${i + 1} de ${parsed.length}…`;
    try {
      await api(`/contracts/${item.contractId}/send`, { method: 'POST' });
      item.result = h('span', { class: 'badge badge--info' }, 'Enviado');
      sent++;
    } catch (err) {
      item.result = h('span', { class: 'badge badge--danger' }, err.message);
      if (err instanceof ApiError && ['quota_exceeded', 'payment_overdue'].includes(err.code)) {
        stopped = err.message;
        item.result = h('span', { class: 'badge badge--neutral' }, 'Ficou como rascunho');
      }
    }
    renderPreview();
  }
  renderPreview();
  progressEl.textContent = stopped
    ? `${sent} ${sent === 1 ? 'enviado' : 'enviados'}; o resto ficou como rascunho. ${stopped}`
    : `${sent} de ${parsed.length} ${sent === 1 ? 'contrato enviado' : 'contratos enviados'}.`;
  toast(sent ? 'Envio em lote concluído.' : 'Nenhum contrato foi enviado.', sent ? 'info' : 'error');
}

createBtn.addEventListener('click', () => create(false));
createSendBtn.addEventListener('click', () => create(true));

// ---------- Início (no fim do módulo: as funções e constantes acima já existem) ----------

billing = await api('/organization/billing');
document.querySelector('[data-main]').hidden = false;
if (!billing.plan.bulkSend) {
  document.querySelector('[data-bulk-plans]').textContent =
    billing.plans.filter((p) => p.bulkSend).map((p) => p.name).join(' e ');
  document.querySelector('[data-upgrade]').hidden = false;
} else {
  document.querySelector('[data-flow]').hidden = false;
  await loadTemplates();
}
