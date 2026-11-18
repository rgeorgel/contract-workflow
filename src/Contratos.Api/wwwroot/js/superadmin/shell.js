// Casca do portal super admin: cabeçalho escuro, menu lateral e checagem de acesso.
import { getMe, logout } from '../auth.js';
import { h, icon, ICONS } from '../ui/dom.js';
import { SA_ICONS } from './common.js';

const LINKS = [
  { group: 'Visão geral' },
  { key: 'overview', href: '/superadmin/', label: 'Painel', icon: SA_ICONS.chart },
  { group: 'Clientes' },
  { key: 'tenants', href: '/superadmin/tenants.html', label: 'Tenants', icon: SA_ICONS.building },
];

/** Usuário logado com acesso ao portal; sem acesso, volta ao app. */
export async function requirePlatformAdmin() {
  const me = await getMe();
  if (!me.platformAdmin) {
    location.replace('/dashboard.html');
    throw new Error('Sem acesso ao portal super admin');
  }
  return me;
}

/** <sa-shell active="tenants">: cabeçalho e menu (display: contents, entra na grade do body). */
class SaShell extends HTMLElement {
  async connectedCallback() {
    const me = await requirePlatformAdmin();
    const active = this.getAttribute('active');
    this.replaceChildren(
      h('header', { class: 'sa-top' },
        h('div', { class: 'sa-top__inner' },
          h('a', { class: 'sa-top__brand', href: '/superadmin/' }, icon(ICONS.logo), h('span', { class: 'sa-top__name' }, 'Contratos'), h('span', { class: 'sa-top__tag' }, 'Super admin')),
          h('div', { class: 'sa-top__right' },
            h('span', { class: 'sa-top__who' }, me.name),
            h('a', { class: 'btn btn--secondary btn--sm', href: '/dashboard.html' }, h('span', { class: 'sa-top__long' }, 'Voltar ao app'), h('span', { class: 'sa-top__short' }, 'App')),
            h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: logout }, 'Sair')))),
      h('aside', { class: 'sa-side' },
        h('nav', { class: 'sa-side__nav', 'aria-label': 'Super admin' },
          LINKS.map((l) => (l.group
            ? h('div', { class: 'sa-side__group', 'aria-hidden': 'true' }, l.group)
            : h('a', { href: l.href, 'aria-current': l.key === active ? 'page' : null }, icon(l.icon), l.label))))));
  }
}

customElements.define('sa-shell', SaShell);
