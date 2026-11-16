// Portal de desenvolvedores: endereço real da API nos exemplos, abas de linguagem, copiar e índice ativo.

const base = window.location.origin;

// Exemplos são escritos com {{BASE_URL}}; aqui viram o endereço de quem está lendo (produção ou testes).
for (const el of document.querySelectorAll('pre code, [data-base-url]')) {
  if (el.textContent.includes('{{BASE_URL}}')) el.textContent = el.textContent.replaceAll('{{BASE_URL}}', base);
}

function copyButton(getText) {
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'code__copy';
  btn.textContent = 'Copiar';
  btn.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(getText());
      btn.textContent = 'Copiado';
    } catch {
      btn.textContent = 'Selecione e copie';
    }
    setTimeout(() => { btn.textContent = 'Copiar'; }, 2000);
  });
  return btn;
}

// Blocos simples com título: botão de copiar no título.
for (const block of document.querySelectorAll('.code:not(.code-tabs)')) {
  const title = block.querySelector('.code__title');
  const pre = block.querySelector('pre');
  if (title && pre) title.append(copyButton(() => pre.textContent));
}

// Abas: uma por linguagem. A escolha vale para a página toda (quem usa Python vê Python em todos).
const tabGroups = [...document.querySelectorAll('.code-tabs')];
function selectLanguage(lang) {
  for (const group of tabGroups) {
    const buttons = [...group.querySelectorAll('[role="tab"]')];
    if (!buttons.some((b) => b.dataset.lang === lang)) continue;
    for (const b of buttons) {
      const selected = b.dataset.lang === lang;
      b.setAttribute('aria-selected', String(selected));
      b.tabIndex = selected ? 0 : -1;
      group.querySelector(`#${b.getAttribute('aria-controls')}`).hidden = !selected;
    }
  }
  try { localStorage.setItem('docs-lang', lang); } catch { /* navegação privada */ }
}

let counter = 0;
for (const group of tabGroups) {
  const panels = [...group.querySelectorAll('pre[data-lang]')];
  const list = document.createElement('div');
  list.className = 'code-tabs__list';
  list.setAttribute('role', 'tablist');
  list.setAttribute('aria-label', group.dataset.label || 'Linguagem');
  for (const pre of panels) {
    const id = `code-panel-${++counter}`;
    pre.id = id;
    pre.setAttribute('role', 'tabpanel');
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.setAttribute('role', 'tab');
    btn.setAttribute('aria-controls', id);
    btn.dataset.lang = pre.dataset.lang;
    btn.textContent = pre.dataset.title || pre.dataset.lang;
    btn.addEventListener('click', () => selectLanguage(pre.dataset.lang));
    btn.addEventListener('keydown', (e) => {
      if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
      const tabs = [...list.querySelectorAll('[role="tab"]')];
      const next = tabs[(tabs.indexOf(btn) + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
      next.focus();
      selectLanguage(next.dataset.lang);
    });
    list.append(btn);
  }
  list.append(copyButton(() => panels.find((p) => !p.hidden)?.textContent ?? ''));
  group.prepend(list);
}

let saved = null;
try { saved = localStorage.getItem('docs-lang'); } catch { /* navegação privada */ }
selectLanguage(saved || 'curl');
for (const group of tabGroups) {
  // Grupo sem a linguagem salva: mostra a primeira aba.
  if (!group.querySelector('[role="tab"][aria-selected="true"]')) {
    const first = group.querySelector('[role="tab"]');
    first.setAttribute('aria-selected', 'true');
    first.tabIndex = 0;
    for (const pre of group.querySelectorAll('pre[data-lang]')) pre.hidden = pre.id !== first.getAttribute('aria-controls');
  }
}

// Índice: destaca a seção visível.
const links = new Map([...document.querySelectorAll('.docs-toc a[href^="#"]')].map((a) => [a.getAttribute('href').slice(1), a]));
const observer = new IntersectionObserver((entries) => {
  for (const entry of entries) {
    if (!entry.isIntersecting) continue;
    for (const a of links.values()) a.classList.remove('is-active');
    links.get(entry.target.id)?.classList.add('is-active');
  }
}, { rootMargin: '-80px 0px -70% 0px' });
for (const id of links.keys()) {
  const el = document.getElementById(id);
  if (el) observer.observe(el);
}

// Celular: o índice começa fechado (no desktop ele é a barra lateral) e fecha ao escolher um item.
if (window.matchMedia('(max-width: 999px)').matches) document.querySelector('.docs-toc details')?.removeAttribute('open');
for (const a of links.values()) a.addEventListener('click', () => a.closest('details')?.removeAttribute('open'));

const header = document.querySelector('[data-header]');
const onScroll = () => header?.classList.toggle('is-scrolled', window.scrollY > 8);
window.addEventListener('scroll', onScroll, { passive: true });
onScroll();
document.querySelector('[data-year]').textContent = String(new Date().getFullYear());
