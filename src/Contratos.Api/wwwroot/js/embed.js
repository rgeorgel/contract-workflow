// Modo embed: a tela de templates dentro do sistema do cliente, num iframe.
// A página abre com /embed/templates?session=<id>#token=<uso único>. O token do link é trocado por um token
// de acesso (POST /api/embed/v1/session), guardado na sessionStorage da aba (ou, sem storage, no # dos links).
// Nada de cookie: o iframe é de outro site e os navegadores bloqueiam cookies de terceiros.

const PAGES = { '/templates.html': '/embed/templates', '/template-editor.html': '/embed/template-editor' };
const params = new URLSearchParams(location.search);

/** Página aberta pelo embed. */
export const EMBED = location.pathname.startsWith('/embed/');
const sessionId = EMBED ? params.get('session') : null;
const storageKey = `contratos.embed.${sessionId}`;

let accessToken = null;
let storageWorks = true;
let session = null;
let parentOrigin = null;

function readStored() {
  try { return sessionStorage.getItem(storageKey); } catch { storageWorks = false; return null; }
}

function store(token) {
  try { sessionStorage.setItem(storageKey, token); } catch { storageWorks = false; }
}

function forget() {
  try { sessionStorage.removeItem(storageKey); } catch { /* sem storage */ }
}

export function embedToken() {
  return accessToken;
}

/** Link interno: as páginas da tela viram as do embed, levando a sessão (e o token, se não houver storage). */
export function href(path) {
  if (!EMBED) return path;
  const url = new URL(path, location.origin);
  const page = PAGES[url.pathname];
  if (!page) return path;
  const query = new URLSearchParams(url.search);
  query.set('session', sessionId);
  const hash = !storageWorks && accessToken ? `#access=${encodeURIComponent(accessToken)}` : '';
  return `${page}?${query}${hash}`;
}

/** Pode fazer isso? Na tela, o perfil Admin; no embed, as permissões da sessão (edit, publish, archive). */
export function can(me, permission) {
  if (me.embed) return me.permissions.includes(permission);
  return me.role === 'Admin';
}

/** Avisa o sistema do cliente (window.parent) do que aconteceu. Só para os sites cadastrados. */
export function notify(type, data = {}) {
  if (!EMBED || window.parent === window || !parentOrigin) return;
  window.parent.postMessage({ source: 'contratos', type, sessionId, ...data }, parentOrigin);
}

function originMatches(pattern, origin) {
  if (pattern === origin) return true;
  const star = pattern.indexOf('://*.');
  if (star < 0) return false;
  const scheme = pattern.slice(0, star + 3);
  const suffix = pattern.slice(star + 4);
  if (!origin.startsWith(scheme) || !origin.endsWith(suffix)) return false;
  const middle = origin.slice(scheme.length, origin.length - suffix.length);
  return middle.length > 0 && !middle.includes(':');
}

/** Site que abriu o iframe: o navegador informa (ancestorOrigins ou referrer); só vale se estiver cadastrado. */
function findParentOrigin(allowed) {
  const candidates = [];
  if (location.ancestorOrigins?.length) candidates.push(location.ancestorOrigins[0]);
  try { if (document.referrer) candidates.push(new URL(document.referrer).origin); } catch { /* referrer inválido */ }
  const match = candidates.find((o) => allowed.some((p) => originMatches(p, o)));
  if (match) return match;
  // Um único domínio exato cadastrado: é ele.
  return allowed.length === 1 && !allowed[0].includes('*') ? allowed[0] : null;
}

/** A sessão terminou (expirou, foi revogada ou o link já tinha sido usado): aviso no lugar da página. */
export function showEnded(message = 'A sessão terminou. Abra de novo pelo seu sistema.') {
  forget();
  notify('session.expired');
  if (document.querySelector('[data-embed-ended]')) return;
  const box = document.createElement('div');
  box.className = 'embed-ended';
  box.setAttribute('data-embed-ended', '');
  box.setAttribute('role', 'alert');
  const title = document.createElement('h1');
  title.textContent = 'Sessão encerrada';
  const text = document.createElement('p');
  text.textContent = message;
  box.append(title, text);
  document.body.replaceChildren(box);
  throw new Error('embed_session_ended');
}

/**
 * Abre a sessão (na primeira página) ou retoma (nas seguintes). Devolve o "usuário" no formato do /auth/me,
 * com as permissões da sessão.
 */
export async function embedMe() {
  if (session) return session;
  document.documentElement.classList.add('is-embed');

  const hash = new URLSearchParams(location.hash.slice(1));
  const launch = hash.get('token');
  accessToken = hash.get('access') || readStored();
  if (hash.has('token') || hash.has('access')) history.replaceState(null, '', location.pathname + location.search);

  let info;
  if (launch) {
    const res = await fetch('/api/embed/v1/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ sessionId, token: launch }),
    });
    if (!res.ok) {
      // O link já foi aberto nesta aba (recarregou): segue com o token guardado.
      if (!accessToken) showEnded('Este link já foi aberto ou expirou. Abra de novo pelo seu sistema.');
    } else {
      const opened = await res.json();
      accessToken = opened.accessToken;
      info = opened.session;
      store(accessToken);
    }
  }
  if (!accessToken) showEnded();
  if (!info) {
    const res = await fetch('/api/embed/v1/session', { headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` } });
    if (!res.ok) showEnded();
    info = await res.json();
  }

  parentOrigin = findParentOrigin(info.allowedOrigins);
  session = {
    embed: true,
    role: 'Embed',
    permissions: info.permissions,
    name: info.user?.name ?? '',
    organization: { name: info.organizationName, paymentOverdue: false },
    expiresAt: info.expiresAt,
  };
  watchHeight();
  // Sem aviso antes: o token para de valer no horário e a próxima chamada mostra a mensagem.
  const msLeft = new Date(info.expiresAt).getTime() - Date.now();
  if (msLeft > 0 && msLeft < 2 ** 31) setTimeout(() => notify('session.expired'), msLeft);
  return session;
}

/** Altura do conteúdo, para o sistema do cliente ajustar o iframe (evento "resize"). */
function watchHeight() {
  let last = 0;
  const send = () => {
    const height = Math.ceil(document.body.scrollHeight);
    if (height !== last) { last = height; notify('resize', { height }); }
  };
  new ResizeObserver(send).observe(document.body);
  send();
}
