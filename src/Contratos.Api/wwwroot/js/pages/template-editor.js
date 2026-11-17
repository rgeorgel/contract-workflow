import { api, ApiError } from '../api.js';
import { requireUser } from '../auth.js';
import { can, EMBED, href, notify } from '../embed.js';
import { clear, h } from '../ui/dom.js';
import { setBusy } from '../ui/form.js';
import { confirmModal } from '../ui/modal.js';
import { RichEditor } from '../ui/rich-editor.js';
import { toast } from '../ui/toast.js';
import { formatDateTime, humanize } from '../utils/format.js';

const TYPES = { Text: 'Texto', Number: 'Número', Date: 'Data', Money: 'Moeda', CpfCnpj: 'CPF/CNPJ' };
const ACTIONS = { Sign: 'Assinar', SignAsWitness: 'Assinar como testemunha', Approve: 'Aprovar', Recognize: 'Reconhecer' };
// {{chave}} e também a chave de um trecho condicional {{#se chave}} … {{/se}}.
const PLACEHOLDER = /\{\{\s*(?:#se\s+)?([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)\s*\}\}/gi;

const me = await requireUser();
// Na tela, Admin pode tudo; no embed, vale o que a sessão permite (publicar e arquivar exigem editar).
const canEdit = can(me, 'edit');
const canPublish = canEdit && can(me, 'publish');
const canArchive = canEdit && can(me, 'archive');
const id = new URLSearchParams(location.search).get('id');
if (!id) location.replace(href('/templates.html'));
document.querySelector('.back-link').href = href('/templates.html');

const form = document.getElementById('editor-form');
const alertEl = document.querySelector('[data-alert]');
const variablesEl = document.querySelector('[data-variables]');
const rolesEl = document.querySelector('[data-roles]');
const insertEl = document.querySelector('[data-insert]');
const frame = document.querySelector('[data-frame]');
const actions = Object.fromEntries([...document.querySelectorAll('[data-action]')].map((b) => [b.dataset.action, b]));

const bodyEl = form.elements.bodyHtml;
const MODE_KEY = 'contratos.templateEditorMode';
let mode = 'visual';

// O textarea continua sendo o campo do formulário; o editor visual escreve nele a cada edição.
const visual = new RichEditor(document.querySelector('[data-visual]'), {
  label: 'Texto do contrato',
  placeholder: 'Escreva o contrato aqui, ou cole do Word ou do Google Docs.',
  onChange: (html) => { bodyEl.value = html; markDirty(); },
});

let template = null;
let etag = null;
let dirty = false;
let previewSource = 'draft';

// ---------- Estado ↔ formulário ----------

/** Conteúdo em edição: o rascunho, ou a versão publicada se não houver rascunho. */
const working = () => template.draft ?? template.current ?? { bodyHtml: '', variables: [], defaultSignerRoles: [] };

function select(options, value, attrs = {}) {
  return h('select', { class: 'select', ...attrs },
    Object.entries(options).map(([v, label]) => h('option', { value: v, selected: v === value }, label)));
}

function removeButton(label, onclick) {
  return h('button', { type: 'button', class: 'icon-btn', 'aria-label': label, title: label, onclick, 'data-edit-only': true }, '×');
}

function variableRow(v = { key: '', label: '', type: 'Text', required: true }) {
  const key = h('input', { class: 'input input--key', value: v.key, 'aria-label': 'Chave', placeholder: 'contratante.nome', 'data-field': 'key' });
  const label = h('input', { class: 'input', value: v.label, 'aria-label': 'Rótulo', placeholder: 'Nome do contratante', 'data-field': 'label' });
  key.addEventListener('blur', () => { if (!label.value.trim() && key.value.trim()) { label.value = humanize(key.value.trim()); markDirty(); } });
  const row = h('tr', {},
    h('td', {}, key),
    h('td', {}, label),
    h('td', {}, select(TYPES, v.type, { 'aria-label': 'Tipo', 'data-field': 'type' })),
    h('td', { class: 'cell-check' }, h('input', { type: 'checkbox', checked: v.required, 'aria-label': 'Obrigatória', 'data-field': 'required' })),
    h('td', {}, removeButton('Remover variável', () => { row.remove(); afterRowsChange(); })));
  return row;
}

function roleRow(r = { role: '', action: 'Sign', order: rolesEl.children.length + 1 }) {
  const row = h('tr', {},
    h('td', {}, h('input', { class: 'input', value: r.role, 'aria-label': 'Papel', placeholder: 'Contratante', 'data-field': 'role' })),
    h('td', {}, select(ACTIONS, r.action, { 'aria-label': 'Ação', 'data-field': 'action' })),
    h('td', {}, h('input', { class: 'input input--order', type: 'number', min: 1, max: 100, value: r.order, 'aria-label': 'Ordem', 'data-field': 'order' })),
    h('td', {}, removeButton('Remover papel', () => { row.remove(); afterRowsChange(); })));
  return row;
}

function readVariables() {
  return [...variablesEl.rows].map((row) => ({
    key: row.querySelector('[data-field="key"]').value.trim(),
    label: row.querySelector('[data-field="label"]').value.trim(),
    type: row.querySelector('[data-field="type"]').value,
    required: row.querySelector('[data-field="required"]').checked,
  }));
}

function readRoles() {
  return [...rolesEl.rows].map((row) => ({
    role: row.querySelector('[data-field="role"]').value.trim(),
    action: row.querySelector('[data-field="action"]').value,
    order: Number(row.querySelector('[data-field="order"]').value) || 1,
  }));
}

function afterRowsChange() {
  document.querySelector('[data-variables-empty]').hidden = variablesEl.rows.length > 0;
  document.querySelector('[data-roles-empty]').hidden = rolesEl.rows.length > 0;
  refreshInsertOptions();
  markDirty();
}

function refreshInsertOptions() {
  const variables = readVariables();
  visual.setVariables(variables);
  const keys = variables.map((v) => v.key).filter(Boolean);
  insertEl.replaceChildren(
    h('option', { value: '' }, keys.length ? 'Inserir variável…' : 'Cadastre variáveis primeiro'),
    ...keys.map((k) => h('option', { value: k }, k)));
}

function fillForm() {
  const content = working();
  form.elements.name.value = template.name;
  form.elements.description.value = template.description ?? '';
  bodyEl.value = content.bodyHtml;
  visual.setHtml(content.bodyHtml);
  variablesEl.replaceChildren(...content.variables.map(variableRow));
  rolesEl.replaceChildren(...content.defaultSignerRoles.map(roleRow));
  afterRowsChange();
  setDirty(false);
  cancelLivePreview();
}

function payload() {
  return {
    name: form.elements.name.value.trim(),
    description: form.elements.description.value.trim() || null,
    bodyHtml: form.elements.bodyHtml.value,
    variables: readVariables(),
    defaultSignerRoles: readRoles(),
  };
}

// ---------- Cabeçalho e estado ----------

function setDirty(value) {
  dirty = value;
  renderStatus();
}
/** Toda edição: marca como não salvo e atualiza a pré-visualização. */
const markDirty = () => {
  if (!template) return;
  if (!dirty) setDirty(true);
  scheduleLivePreview();
};

function renderStatus() {
  document.querySelector('[data-title]').textContent = template.name;
  document.title = `${template.name} · Contratos`;
  const badges = [];
  if (template.archivedAt) badges.push(h('span', { class: 'badge badge--neutral' }, 'Arquivado'));
  if (template.current) badges.push(h('span', { class: 'badge badge--success' }, `Publicado · v${template.current.version}`));
  if (template.draft) badges.push(h('span', { class: 'badge badge--warning' }, `Rascunho da v${template.draft.version}`));
  if (dirty) badges.push(h('span', { class: 'dirty-dot' }, 'Alterações não salvas'));
  document.querySelector('[data-status]').replaceChildren(...badges);

  if (!canEdit) return;
  const archived = Boolean(template.archivedAt);
  actions.archive.hidden = !canArchive;
  actions.archive.textContent = archived ? 'Desarquivar' : 'Arquivar';
  actions.discard.hidden = !(template.draft && template.current);
  actions.save.hidden = archived;
  actions.publish.hidden = archived || !canPublish;
  actions.publish.textContent = `Publicar v${template.draft?.version ?? (template.current?.version ?? 0) + 1}`;
  actions.publish.disabled = !template.draft && !dirty;
}

function renderVersions() {
  const list = document.querySelector('[data-versions]');
  clear(list);
  for (const v of template.versions) {
    list.append(h('li', {},
      h('strong', {}, `v${v.version}`),
      v.publishedAt
        ? h('span', {}, `Publicada em ${formatDateTime(v.publishedAt)}`, v.createdByName ? ` · ${v.createdByName}` : '')
        : h('span', { class: 'badge badge--warning' }, 'Rascunho'),
      v.id === template.current?.id ? h('span', { class: 'badge badge--success' }, 'Em uso') : null));
  }
}

function apply(detail, newEtag) {
  template = detail;
  if (newEtag) etag = newEtag;
  fillForm();
  renderVersions();
  reloadPreview();
}

async function reloadPreview() {
  frameReady = false;
  if (!EMBED) {
    frame.src = `/api/v1/templates/${id}/preview?source=${previewSource}&t=${Date.now()}`;
    return;
  }
  // O iframe não manda o token da sessão: o embed pede um link com ticket curto.
  try {
    const link = await api(`/templates/${id}/preview-link?source=${previewSource}`, { method: 'POST' });
    frame.src = link.url;
  } catch {
    previewNote.textContent = 'Não foi possível carregar a pré-visualização agora.';
  }
}

// ---------- Pré-visualização ao vivo ----------
// O iframe carrega a página salva (com a CSP própria da pré-visualização). A cada edição, o servidor
// renderiza o conteúdo do formulário e só o <body> do iframe é trocado: a rolagem fica onde estava.

const LIVE_DELAY_MS = 500;
let frameReady = false;
let liveTimer = null;
let liveAbort = null;
let livePending = false;

function cancelLivePreview() {
  clearTimeout(liveTimer);
  liveAbort?.abort();
  livePending = false;
}

function scheduleLivePreview() {
  if (!canEdit || previewSource !== 'draft') return;
  clearTimeout(liveTimer);
  liveTimer = setTimeout(renderLivePreview, LIVE_DELAY_MS);
}

async function renderLivePreview() {
  if (!frameReady) { livePending = true; return; }
  liveAbort?.abort();
  const controller = new AbortController();
  liveAbort = controller;
  const { name, bodyHtml, variables } = payload();
  try {
    const html = await api('/templates/preview', { method: 'POST', body: { name, bodyHtml, variables }, signal: controller.signal, as: 'text' });
    if (controller.signal.aborted || previewSource !== 'draft') return;
    const doc = frame.contentDocument;
    if (!doc?.body) return;
    // O parser do próprio iframe: o HTML fica sob a CSP da pré-visualização, não a da página.
    const rendered = new frame.contentWindow.DOMParser().parseFromString(html, 'text/html');
    doc.body.replaceChildren(...[...rendered.body.childNodes].map((n) => doc.importNode(n, true)));
    previewNote.textContent = NOTE_LIVE;
  } catch (err) {
    if (err.name === 'AbortError') return;
    // Falhou (ex.: limite de requisições): fica a última versão boa e o aviso.
    previewNote.textContent = 'Não foi possível atualizar a pré-visualização agora. Ela volta na próxima edição.';
  }
}

const previewNote = document.querySelector('[data-preview-note]');
const NOTE_LIVE = 'Com dados de exemplo. Atualiza enquanto você edita.';

frame.addEventListener('load', () => {
  frameReady = true;
  // Voltando para "Rascunho" com alterações não salvas, ou edição feita enquanto carregava.
  if (livePending || (dirty && previewSource === 'draft' && canEdit)) {
    livePending = false;
    renderLivePreview();
  }
});

// ---------- Erros ----------

function showErrors(err) {
  for (const el of form.querySelectorAll('[aria-invalid]')) el.removeAttribute('aria-invalid');
  for (const slot of form.querySelectorAll('[data-error-for]')) slot.textContent = '';

  const fieldErrors = err instanceof ApiError ? err.errors : {};
  const messages = [];
  for (const [key, msgs] of Object.entries(fieldErrors)) {
    const msg = msgs[0];
    const rowMatch = key.match(/^(variables|defaultSignerRoles)\[(\d+)\]\.(\w+)$/);
    if (rowMatch) {
      const [, list, index, field] = rowMatch;
      const tbody = list === 'variables' ? variablesEl : rolesEl;
      tbody.rows[Number(index)]?.querySelector(`[data-field="${field}"]`)?.setAttribute('aria-invalid', 'true');
      messages.push(`${list === 'variables' ? 'Variável' : 'Papel'} ${Number(index) + 1}: ${msg}`);
      continue;
    }
    const slot = form.querySelector(`[data-error-for="${key}"]`);
    if (slot) {
      slot.textContent = msg;
      form.elements.namedItem(key)?.setAttribute?.('aria-invalid', 'true');
    } else {
      messages.push(msg);
    }
  }

  if (bodyEl.getAttribute('aria-invalid')) visual.area.setAttribute('aria-invalid', 'true');

  const title = err.message || 'Não foi possível salvar.';
  alertEl.replaceChildren(h('div', { class: 'alert__body' },
    h('strong', {}, title),
    messages.length ? h('ul', { class: 'error-list' }, messages.map((m) => h('li', {}, m))) : null));
  alertEl.hidden = false;
  alertEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

function clearAlert() {
  alertEl.hidden = true;
  for (const el of form.querySelectorAll('[aria-invalid]')) el.removeAttribute('aria-invalid');
  for (const slot of form.querySelectorAll('[data-error-for]')) slot.textContent = '';
}

// ---------- Ações ----------

async function save({ quiet = false } = {}) {
  clearAlert();
  try {
    const res = await api(`/templates/${id}`, { method: 'PUT', body: payload(), ifMatch: etag, withEtag: true });
    apply(res.body, res.etag);
    notify('template.saved', { template: summary() });
    if (!quiet) toast('Rascunho salvo.');
    return true;
  } catch (err) {
    if (err instanceof ApiError && err.status === 412) {
      showErrors(new ApiError(412, { title: 'Outra pessoa alterou este template enquanto você editava. Copie o que precisar e recarregue a página.' }));
    } else {
      showErrors(err);
    }
    return false;
  }
}

/** O que vai nos avisos ao sistema do cliente (embed). */
function summary() {
  return {
    id: template.id,
    name: template.name,
    publishedVersion: template.current?.version ?? null,
    draftVersion: template.draft?.version ?? null,
    archived: Boolean(template.archivedAt),
  };
}

async function run(button, fn) {
  setBusy(button, true);
  try { await fn(); } finally { setBusy(button, false); renderStatus(); }
}

actions.save.addEventListener('click', () => run(actions.save, () => save()));

actions.publish.addEventListener('click', () => run(actions.publish, async () => {
  if (dirty && !(await save({ quiet: true }))) return;
  const next = template.draft?.version;
  const ok = await confirmModal({
    title: `Publicar a versão ${next}?`,
    description: 'Contratos novos passam a usar esta versão. Os contratos já criados continuam com a versão que usaram.',
    confirmLabel: 'Publicar',
  });
  if (!ok) return;
  try {
    apply(await api(`/templates/${id}/publish`, { method: 'POST' }));
    notify('template.published', { template: summary() });
    toast(`Versão ${next} publicada.`);
  } catch (err) {
    showErrors(err);
  }
}));

actions.discard.addEventListener('click', () => run(actions.discard, async () => {
  const ok = await confirmModal({
    title: 'Descartar o rascunho?',
    description: `As alterações feitas desde a v${template.current.version} serão perdidas.`,
    confirmLabel: 'Descartar', danger: true,
  });
  if (!ok) return;
  try { apply(await api(`/templates/${id}/draft`, { method: 'DELETE' })); toast('Rascunho descartado.'); }
  catch (err) { showErrors(err); }
}));

actions.duplicate.addEventListener('click', () => run(actions.duplicate, async () => {
  try {
    const copy = await api(`/templates/${id}/duplicate`, { method: 'POST' });
    notify('template.created', { template: { id: copy.id, name: copy.name } });
    location.assign(href(`/template-editor.html?id=${copy.id}`));
  } catch (err) { showErrors(err); }
}));

actions.archive.addEventListener('click', () => run(actions.archive, async () => {
  const archived = Boolean(template.archivedAt);
  try {
    apply(await api(`/templates/${id}/${archived ? 'unarchive' : 'archive'}`, { method: 'POST' }));
    notify(archived ? 'template.unarchived' : 'template.archived', { template: summary() });
    toast(archived ? 'Template desarquivado.' : 'Template arquivado. Ele some da lista de novos contratos.');
  } catch (err) { showErrors(err); }
}));

document.querySelector('[data-add-variable]').addEventListener('click', () => {
  const row = variableRow();
  variablesEl.append(row);
  afterRowsChange();
  row.querySelector('input').focus();
});

document.querySelector('[data-add-role]').addEventListener('click', () => {
  const row = roleRow();
  rolesEl.append(row);
  afterRowsChange();
  row.querySelector('input').focus();
});

document.querySelector('[data-detect]').addEventListener('click', () => {
  const declared = new Set(readVariables().map((v) => v.key));
  const found = [...new Set([...form.elements.bodyHtml.value.matchAll(PLACEHOLDER)].map((m) => m[1]))];
  const missing = found.filter((k) => !declared.has(k));
  for (const key of missing) variablesEl.append(variableRow({ key, label: humanize(key), type: 'Text', required: true }));
  if (missing.length) afterRowsChange();
  toast(missing.length
    ? `${missing.length} ${missing.length === 1 ? 'variável cadastrada' : 'variáveis cadastradas'}. Confira os tipos.`
    : found.length ? 'Todas as variáveis do texto já estão cadastradas.' : 'Nenhuma {{variável}} encontrada no texto.');
});

insertEl.addEventListener('change', () => {
  const key = insertEl.value;
  if (!key) return;
  if (mode === 'visual') {
    visual.insertVariable(key);
    insertEl.value = '';
    return;
  }
  const ta = form.elements.bodyHtml;
  const { selectionStart: start, selectionEnd: end, value } = ta;
  const snippet = `{{${key}}}`;
  ta.value = value.slice(0, start) + snippet + value.slice(end);
  ta.focus();
  ta.setSelectionRange(start + snippet.length, start + snippet.length);
  insertEl.value = '';
  markDirty();
});

for (const tab of document.querySelectorAll('[data-preview]')) {
  tab.addEventListener('click', () => {
    previewSource = tab.dataset.preview;
    for (const t of document.querySelectorAll('[data-preview]')) t.setAttribute('aria-selected', String(t === tab));
    cancelLivePreview();
    previewNote.textContent = previewSource === 'draft' && canEdit ? NOTE_LIVE : 'Versão publicada, com dados de exemplo.';
    reloadPreview();
  });
}

form.addEventListener('input', (e) => {
  if (e.target.dataset?.field === 'label' || e.target.dataset?.field === 'key') visual.setVariables(readVariables());
  markDirty();
});
form.addEventListener('change', (e) => { if (e.target.dataset.field === 'key') refreshInsertOptions(); markDirty(); });
form.addEventListener('submit', (e) => e.preventDefault());
document.addEventListener('keydown', (e) => {
  if ((e.metaKey || e.ctrlKey) && e.key === 's' && canEdit) { e.preventDefault(); actions.save.click(); }
});
window.addEventListener('beforeunload', (e) => { if (dirty) e.preventDefault(); });

// ---------- Modo visual / HTML ----------

function setMode(next, { remember = true } = {}) {
  mode = next === 'html' ? 'html' : 'visual';
  // O textarea é a fonte: ao voltar para o visual, mostra o que foi escrito em HTML.
  if (mode === 'visual') visual.setHtml(bodyEl.value);
  document.querySelector('[data-visual]').hidden = mode !== 'visual';
  document.querySelector('[data-visual-hint]').hidden = mode !== 'visual';
  document.querySelector('[data-html-field]').hidden = mode !== 'html';
  for (const tab of document.querySelectorAll('[data-mode]')) tab.setAttribute('aria-selected', String(tab.dataset.mode === mode));
  if (remember) { try { localStorage.setItem(MODE_KEY, mode); } catch { /* sem storage: só não lembra */ } }
}

for (const tab of document.querySelectorAll('[data-mode]')) {
  tab.addEventListener('click', () => { if (tab.dataset.mode !== mode) setMode(tab.dataset.mode); });
}

// ---------- Carga ----------

try {
  const res = await api(`/templates/${id}`, { withEtag: true });
  etag = res.etag;
  template = res.body;
} catch (err) {
  document.querySelector('[data-main]').hidden = false;
  showErrors(err.status === 404 ? new ApiError(404, { title: 'Template não encontrado.' }) : err);
  throw err;
}

if (!canEdit) {
  // Leitores e editores veem a versão publicada, sem edição.
  previewSource = 'current';
  template = { ...template, draft: null };
  document.querySelector('[data-actions]').hidden = true;
  const note = document.querySelector('[data-readonly-note]');
  if (EMBED) note.textContent = 'Você está vendo a versão publicada. Este acesso é só de leitura.';
  note.hidden = false;
  document.querySelector('[data-preview="draft"]').hidden = true;
  document.querySelector('[data-preview="current"]').setAttribute('aria-selected', 'true');
  previewNote.textContent = 'Versão publicada, com dados de exemplo.';
}

let savedMode = 'visual';
try { savedMode = localStorage.getItem(MODE_KEY) ?? 'visual'; } catch { /* sem storage */ }

apply(template);
setMode(canEdit ? savedMode : 'visual', { remember: false });

if (!canEdit) {
  for (const el of form.elements) el.disabled = true;
  visual.setEditable(false);
  for (const el of document.querySelectorAll('[data-edit-only]')) el.hidden = true;
}

document.querySelector('[data-main]').hidden = false;
notify('ready', { page: 'template-editor', template: summary() });
