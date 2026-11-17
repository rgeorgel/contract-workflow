import { api, resetCsrf } from './api.js';
import { EMBED, embedMe } from './embed.js';

let mePromise = null;

/** Usuário logado (uma chamada por página). Sem sessão, o api.js manda para o login. */
export function getMe() {
  // No embed não há usuário logado: a sessão traz a organização e as permissões.
  mePromise ??= EMBED ? embedMe() : api('/auth/me');
  return mePromise;
}

/** Garante sessão e, opcionalmente, um dos perfis; sem permissão volta ao painel. */
export async function requireUser({ roles } = {}) {
  const me = await getMe();
  if (roles && !roles.includes(me.role)) {
    if (EMBED) throw new Error('Página fora do embed');
    location.replace('/dashboard.html');
    throw new Error('Sem permissão');
  }
  return me;
}

export async function logout() {
  try {
    await api('/auth/logout', { method: 'POST', redirectOn401: false });
  } finally {
    resetCsrf();
    location.assign('/login.html');
  }
}

export const ROLES = {
  Admin: { label: 'Admin', description: 'Gerencia usuários, templates e configurações; cria e envia contratos.' },
  Editor: { label: 'Editor', description: 'Cria, envia e acompanha contratos.' },
  Leitor: { label: 'Leitor', description: 'Só consulta contratos e templates.' },
};

export const roleLabel = (role) => ROLES[role]?.label ?? role;
export const isAdmin = (me) => me.role === 'Admin';
export const canEditContracts = (me) => me.role === 'Admin' || me.role === 'Editor';
