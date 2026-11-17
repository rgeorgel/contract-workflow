// Lado do navegador do "CRM": pede a sessão ao próprio servidor, mostra o iframe e escuta os avisos.

const $ = (id) => document.getElementById(id);
const form = $('session-form');
const iframe = $('embed');
const log = $('log');
let config = null;
let session = null;

async function call(method, path, body) {
  const res = await fetch(path, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const fields = data?.errors ? ' ' + Object.values(data.errors).flat().join(' ') : '';
    throw new Error(`${res.status} ${data?.code ?? ''}: ${data?.title ?? 'erro'}${fields}`);
  }
  return data;
}

function showError(err) {
  $('error').textContent = err ? err.message : '';
  $('error').hidden = !err;
}

function addLog(type, data) {
  const li = document.createElement('li');
  const time = new Date().toLocaleTimeString('pt-BR');
  const { source, type: _t, sessionId, ...rest } = data ?? {};
  li.innerHTML = `<span class="log__time"></span> <code class="log__type"></code> <span class="log__data"></span>`;
  li.querySelector('.log__time').textContent = time;
  li.querySelector('.log__type').textContent = type;
  li.querySelector('.log__data').textContent = Object.keys(rest).length ? JSON.stringify(rest) : '';
  if (type === 'session.expired' || type === 'erro') li.classList.add('log__item--warn');
  log.prepend(li);
}

async function loadTemplates() {
  try {
    const templates = await call('GET', '/api/templates');
    const list = $('templates');
    list.replaceChildren(...templates.map((t) => {
      const li = document.createElement('li');
      li.textContent = `${t.name} · v${t.version} · ${t.variableCount} variáve${t.variableCount === 1 ? 'l' : 'is'}`;
      return li;
    }));
    if (templates.length === 0) list.innerHTML = '<li class="hint">Nenhum template publicado ainda.</li>';

    // Opções do "abrir direto no template".
    const select = form.elements.templateId;
    const current = select.value;
    select.replaceChildren(new Option('— lista de templates —', ''),
      ...templates.map((t) => new Option(t.name, t.id, false, t.id === current)));
  } catch (err) {
    showError(err);
  }
}

function renderSession() {
  const info = $('session-info');
  $('end-session').disabled = !session;
  if (!session) { info.hidden = true; return; }
  const expires = new Date(session.expiresAt).toLocaleTimeString('pt-BR');
  info.innerHTML = '<dt>Sessão</dt><dd><code></code></dd><dt>Permissões</dt><dd></dd><dt>Vence às</dt><dd></dd>';
  const dds = info.querySelectorAll('dd');
  dds[0].querySelector('code').textContent = session.id;
  dds[1].textContent = session.permissions.length ? session.permissions.join(', ') : 'só leitura';
  dds[2].textContent = expires;
  info.hidden = false;
}

async function openEmbed() {
  showError(null);
  const permissions = [...form.querySelectorAll('[name=permissions]:checked')].map((c) => c.value);
  try {
    session = await call('POST', '/api/session', {
      permissions,
      userName: form.elements.userName.value.trim(),
      expiresInMinutes: Number(form.elements.expiresInMinutes.value) || undefined,
      templateId: form.elements.templateId.value || undefined,
    });
  } catch (err) {
    showError(err);
    return;
  }
  renderSession();
  iframe.classList.toggle('auto-height', form.elements.autoHeight.checked);
  iframe.style.removeProperty('height');
  iframe.src = session.url; // o link abre uma vez só: cada clique cria uma sessão nova
  iframe.hidden = false;
  $('placeholder').hidden = true;
  addLog('sessão criada', { id: session.id });
}

form.addEventListener('submit', (e) => { e.preventDefault(); openEmbed(); });

$('end-session').addEventListener('click', async () => {
  if (!session) return;
  try {
    await call('DELETE', `/api/session/${session.id}`);
    addLog('sessão encerrada pelo CRM', { id: session.id });
  } catch (err) {
    showError(err);
  }
});

$('reload-templates').addEventListener('click', loadTemplates);
$('clear-log').addEventListener('click', () => log.replaceChildren());

window.addEventListener('message', (event) => {
  // Só aceita mensagens do Contratos.
  if (!config || event.origin !== config.contratosOrigin || event.data?.source !== 'contratos') return;
  const { type } = event.data;
  if (type === 'resize') {
    if (form.elements.autoHeight.checked) iframe.style.height = `${event.data.height}px`;
    return; // não polui o log
  }
  addLog(type, event.data);
  if (type === 'template.published' || type === 'template.archived' || type === 'template.unarchived') loadTemplates();
  if (type === 'session.expired') { session = null; renderSession(); }
});

try {
  config = await call('GET', '/api/config');
  const account = await call('GET', '/api/account');
  $('account').textContent = `Conectado a ${config.contratosUrl} · conta “${account.organization.name}” (plano ${account.organization.planName}).`;
  await loadTemplates();
} catch (err) {
  showError(err);
}
