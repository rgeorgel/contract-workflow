// Cliente da API do portal super admin: /superadmin/api/v1, token antiforgery próprio e 401 → login do portal.
// Independente do app dos clientes (outro login, outro cookie).

const BASE = '/superadmin/api/v1';
let csrfToken = null;

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.title || `Erro ${status}`);
    this.status = status;
    this.problem = problem;
    this.code = problem?.code ?? null;
    this.errors = problem?.errors ?? {};
  }
}

/** O token antiforgery é ligado à conta: descarte ao entrar ou sair. */
export function resetCsrf() {
  csrfToken = null;
}

async function readProblem(res) {
  if ((res.headers.get('content-type') || '').includes('json')) {
    try { return await res.json(); } catch { /* corpo vazio */ }
  }
  if (res.status === 429) return { title: 'Muitas tentativas em pouco tempo. Aguarde um minuto e tente de novo.' };
  return null;
}

async function getCsrf() {
  if (!csrfToken) {
    const res = await fetch(`${BASE}/auth/csrf`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!res.ok) throw new ApiError(res.status, await readProblem(res));
    csrfToken = (await res.json()).token;
  }
  return csrfToken;
}

export async function api(path, { method = 'GET', body, signal, redirectOn401 = true } = {}, retried = false) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (!['GET', 'HEAD'].includes(method)) headers['X-CSRF-TOKEN'] = await getCsrf();

  let res;
  try {
    res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: 'same-origin', signal });
  } catch (err) {
    if (err.name === 'AbortError') throw err;
    throw new ApiError(0, { title: 'Sem conexão com o servidor. Verifique sua internet e tente de novo.' });
  }
  if (res.ok) return res.status === 204 ? null : res.json();

  const problem = await readProblem(res);
  if (problem?.code === 'csrf_invalid' && !retried) {
    resetCsrf();
    return api(path, { method, body, signal, redirectOn401 }, true);
  }
  if (res.status === 401 && redirectOn401) {
    location.assign(`/superadmin/login.html?next=${encodeURIComponent(location.pathname + location.search)}`);
  }
  throw new ApiError(res.status, problem);
}

/** Só caminhos do próprio portal, para o ?next= não virar redirecionamento aberto nem levar ao app. */
export function safeNext(value) {
  const ok = typeof value === 'string' && value.startsWith('/superadmin/') && !value.startsWith('//') && !value.includes('\\')
    && !value.startsWith('/superadmin/api') && !value.startsWith('/superadmin/login');
  return ok ? value : '/superadmin/';
}
