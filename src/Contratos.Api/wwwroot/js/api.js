// Wrapper de fetch: JSON, erros ProblemDetails, token antiforgery e 401 → login.

const BASE = '/api/v1';
let csrfToken = null;

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.title || `Erro ${status}`);
    this.status = status;
    this.problem = problem;
    this.code = problem?.code ?? null;
    /** Erros por campo (ValidationProblemDetails). */
    this.errors = problem?.errors ?? {};
  }
}

/** O token antiforgery muda quando o usuário entra ou sai; descarte-o nessas horas. */
export function resetCsrf() {
  csrfToken = null;
}

async function getCsrf() {
  if (!csrfToken) {
    const res = await fetch(`${BASE}/auth/csrf`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!res.ok) throw new ApiError(res.status, await readProblem(res));
    csrfToken = (await res.json()).token;
  }
  return csrfToken;
}

async function readProblem(res) {
  const type = res.headers.get('content-type') || '';
  if (type.includes('json')) {
    try { return await res.json(); } catch { /* corpo vazio */ }
  }
  if (res.status === 429) return { title: 'Muitas tentativas em pouco tempo. Aguarde um minuto e tente de novo.' };
  return null;
}

/**
 * @param {string} path rota sob /api/v1, ex.: '/auth/me'
 * @param {{method?: string, body?: unknown, signal?: AbortSignal, redirectOn401?: boolean}} [options]
 */
export async function api(path, { method = 'GET', body, signal, redirectOn401 = true } = {}, retried = false) {
  const headers = { Accept: 'application/json' };
  const unsafe = !['GET', 'HEAD'].includes(method);
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (unsafe) headers['X-CSRF-TOKEN'] = await getCsrf();

  let res;
  try {
    res = await fetch(BASE + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin',
      signal,
    });
  } catch (err) {
    if (err.name === 'AbortError') throw err;
    throw new ApiError(0, { title: 'Sem conexão com o servidor. Verifique sua internet e tente de novo.' });
  }

  if (res.ok) {
    if (res.status === 204) return null;
    const type = res.headers.get('content-type') || '';
    return type.includes('json') ? res.json() : null;
  }

  const problem = await readProblem(res);

  // Token antiforgery velho (ex.: a sessão mudou em outra aba): pega outro e tenta uma vez.
  if (problem?.code === 'csrf_invalid' && !retried) {
    resetCsrf();
    return api(path, { method, body, signal, redirectOn401 }, true);
  }

  if (res.status === 401 && redirectOn401) {
    const next = encodeURIComponent(location.pathname + location.search);
    location.assign(`/login.html?next=${next}`);
  }

  throw new ApiError(res.status, problem);
}

/** Só aceita caminhos internos, para o ?next= não virar redirecionamento aberto. */
export function safeNext(value, fallback = '/dashboard.html') {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return fallback;
  return value;
}
