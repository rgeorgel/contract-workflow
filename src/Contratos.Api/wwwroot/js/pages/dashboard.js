import { isAdmin, requireUser } from '../auth.js';
import { h } from '../ui/dom.js';

const me = await requireUser();

document.querySelector('[data-first-name]').textContent = me.name.split(' ')[0];
document.querySelector('[data-org-name]').textContent = me.organization.name;

const steps = [
  { title: 'Montar o primeiro template', text: 'Cole o modelo de contrato e marque as variáveis.', href: '/templates.html' },
  { title: 'Gerar e enviar um contrato', text: 'Preencha os dados, adicione quem assina e envie.' },
  isAdmin(me)
    ? { title: 'Convidar a equipe', text: 'Defina quem é Admin, Editor ou Leitor.', href: '/users.html' }
    : { title: 'Conferir as configurações', text: 'Veja os padrões de envio da sua empresa.', href: '/settings.html' },
];

document.querySelector('[data-steps]').replaceChildren(...steps.map((s, i) => {
  const content = [
    h('span', { class: 'step-badge' }, String(i + 1)),
    h('div', {}, h('h2', {}, s.title), h('p', { class: 'muted' }, s.text)),
    s.href ? h('span', { class: 'badge badge--info' }, 'Abrir') : h('span', { class: 'badge badge--neutral' }, 'Em breve'),
  ];
  return h('li', {}, s.href ? h('a', { class: 'card next-step', href: s.href }, content) : h('div', { class: 'card next-step' }, content));
}));

document.querySelector('[data-main]').hidden = false;
