import { getMe, logout, roleLabel } from '../auth.js';
import { h, icon, ICONS } from '../ui/dom.js';

const LINKS = [
  { key: 'dashboard', href: '/dashboard.html', label: 'Painel' },
  { key: 'templates', href: '/templates.html', label: 'Templates' },
  { key: 'users', href: '/users.html', label: 'Usuários', roles: ['Admin'] },
  { key: 'settings', href: '/settings.html', label: 'Configurações' },
];

/** <app-nav active="users">: cabeçalho da área logada, com links conforme o perfil. */
class AppNav extends HTMLElement {
  async connectedCallback() {
    const me = await getMe();
    const active = this.getAttribute('active');
    const links = LINKS.filter((l) => !l.roles || l.roles.includes(me.role));
    const linkEl = (l) => h('a', { href: l.href, 'aria-current': l.key === active ? 'page' : null }, l.label);

    const drawer = h('nav', { class: 'app-nav__drawer', id: 'app-nav-drawer', 'aria-label': 'Menu', hidden: true },
      links.map(linkEl));
    const toggle = h('button', {
      type: 'button', class: 'btn btn--ghost btn--sm app-nav__toggle',
      'aria-expanded': 'false', 'aria-controls': 'app-nav-drawer', 'aria-label': 'Abrir menu',
      onclick: () => {
        drawer.hidden = !drawer.hidden;
        toggle.setAttribute('aria-expanded', String(!drawer.hidden));
      },
    }, icon(ICONS.menu));

    this.replaceChildren(h('header', { class: 'app-nav' },
      h('div', { class: 'container app-nav__inner' },
        h('a', { class: 'app-nav__brand', href: '/dashboard.html' }, icon(ICONS.logo), h('span', { class: 'app-nav__org' }, me.organization.name)),
        h('nav', { class: 'app-nav__links', 'aria-label': 'Principal' }, links.map(linkEl)),
        h('div', { class: 'app-nav__user' },
          h('span', { class: 'app-nav__who' }, h('strong', {}, me.name), h('small', {}, roleLabel(me.role))),
          h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: logout }, 'Sair'),
          toggle)),
      drawer));
  }
}

customElements.define('app-nav', AppNav);
