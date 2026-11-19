import { api, resetCsrf } from '../api.js';
import { getMe, logout, roleLabel } from '../auth.js';
import { EMBED } from '../embed.js';
import { h, icon, ICONS } from '../ui/dom.js';

const LINKS = [
  { key: 'dashboard', href: '/dashboard.html', label: 'Painel' },
  { key: 'contracts', href: '/contracts.html', label: 'Contratos' },
  { key: 'templates', href: '/templates.html', label: 'Templates' },
  { key: 'users', href: '/users.html', label: 'Usuários', roles: ['Admin'] },
  { key: 'integrations', href: '/integrations.html', label: 'Integrações', roles: ['Admin'] },
  { key: 'settings', href: '/settings.html', label: 'Configurações' },
];

/** <app-nav active="users">: cabeçalho da área logada, com links conforme o perfil. */
class AppNav extends HTMLElement {
  async connectedCallback() {
    // Dentro do sistema do cliente, a navegação é dele.
    if (EMBED) { this.hidden = true; return; }
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

    // replaceChildren escreve null como texto: só entra o que existe (o aviso aparece só com pagamento pendente).
    this.replaceChildren(...[h('header', { class: 'app-nav' },
      h('div', { class: 'container app-nav__inner' },
        h('a', { class: 'app-nav__brand', href: '/dashboard.html' }, icon(ICONS.logo), h('span', { class: 'app-nav__org' }, me.organization.name)),
        h('nav', { class: 'app-nav__links', 'aria-label': 'Principal' }, links.map(linkEl)),
        h('div', { class: 'app-nav__user' },
          h('span', { class: 'app-nav__who' }, h('strong', {}, me.name), h('small', {}, roleLabel(me.role))),
          themeToggle(),
          h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: logout }, 'Sair'),
          toggle)),
      drawer),
    impersonationBanner(me),
    overdueBanner(me)].filter(Boolean));
  }
}

/** Alterna o tema claro/escuro (js/theme.js aplica e guarda a escolha). O ícone mostra o tema que o clique ativa. */
function themeToggle() {
  const theme = window.contratosTheme;
  if (!theme) return null;
  const btn = h('button', {
    type: 'button', class: 'btn btn--ghost btn--sm app-nav__theme',
    onclick: () => theme.set(theme.get() === 'dark' ? 'light' : 'dark'),
  });
  const render = () => {
    const dark = theme.get() === 'dark';
    const label = dark ? 'Ativar tema claro' : 'Ativar tema escuro';
    btn.setAttribute('aria-label', label);
    btn.title = label;
    btn.replaceChildren(icon(dark ? ICONS.sun : ICONS.moon));
  };
  render();
  document.addEventListener('themechange', render);
  return btn;
}

const timeFmt =new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' });

/** Sessão de suporte ("Entrar como"): alguém da equipe está usando esta conta. Sempre visível, com o botão de encerrar. */
function impersonationBanner(me) {
  if (!me.impersonation) return null;
  const back = h('button', {
    type: 'button', class: 'btn btn--sm impersonation-banner__btn',
    onclick: async () => {
      back.disabled = true;
      try {
        const res = await api('/auth/stop-impersonation', { method: 'POST' });
        resetCsrf();
        location.assign(res.redirect);
      } catch {
        location.assign('/login.html');
      }
    },
  }, 'Encerrar sessão de suporte');
  const until = me.impersonation.expiresAt ? ` Sessão até ${timeFmt.format(new Date(me.impersonation.expiresAt))}.` : '';
  return h('div', { class: 'impersonation-banner', role: 'status' },
    h('div', { class: 'container impersonation-banner__inner' },
      h('span', {},
        h('strong', {}, `Sessão de suporte: ${me.impersonation.impersonatorName} está usando a conta de ${me.name}`),
        ` (${me.organization.name}). Cada alteração fica registrada.${until}`),
      back));
}

/** Pagamento da assinatura recusado: aviso em todas as páginas, com o que está bloqueado e como regularizar. */
function overdueBanner(me) {
  if (!me.organization.paymentOverdue) return null;
  const admin = me.role === 'Admin';
  return h('div', { class: 'overdue-banner', role: 'alert' },
    h('div', { class: 'container overdue-banner__inner' },
      h('span', {},
        h('strong', {}, 'Pagamento da assinatura pendente. '),
        'Criar contratos, templates e usuários está bloqueado até o pagamento ser aprovado; os contratos existentes continuam disponíveis.',
        admin ? null : ' Peça a um Admin para atualizar o cartão.'),
      admin ? h('a', { class: 'btn btn--sm overdue-banner__btn', href: '/settings.html' }, 'Atualizar pagamento') : null));
}

customElements.define('app-nav', AppNav);
