// "Novo template": galeria de modelos prontos por setor → conferir o modelo e dar nome → editor.
import { api, ApiError } from '../api.js';
import { href, notify } from '../embed.js';
import { clear, h, icon } from '../ui/dom.js';
import { applyErrors, clearErrors, setBusy, setFieldError, showAlert } from '../ui/form.js';
import { openModal } from '../ui/modal.js';

const svg = (paths) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;

/** Ícone de cada setor (marcação fixa daqui; setor novo sem ícone usa o de documento). */
const SECTOR_ICONS = {
  all: svg('<rect x="4" y="4" width="7" height="7" rx="1.5"/><rect x="13" y="4" width="7" height="7" rx="1.5"/><rect x="4" y="13" width="7" height="7" rx="1.5"/><rect x="13" y="13" width="7" height="7" rx="1.5"/>'),
  geral: svg('<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>'),
  advocacia: svg('<path d="M12 4v16M7 20h10M5 7h14"/><path d="m5 7-3 6a3 3 0 0 0 6 0zM19 7l-3 6a3 3 0 0 0 6 0z"/>'),
  imobiliario: svg('<path d="m3 11 9-7 9 7"/><path d="M5 9.5V20h14V9.5"/><path d="M10 20v-6h4v6"/>'),
  contabilidade: svg('<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 7h8M8 11.5h.01M12 11.5h.01M16 11.5h.01M8 15.5h.01M12 15.5h.01M16 15.5v2.5M8 18.5h.01M12 18.5h.01"/>'),
  rh: svg('<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 7M21.5 20a6.5 6.5 0 0 0-4-6"/>'),
  comercial: svg('<path d="M5 8h14l-1.2 12H6.2z"/><path d="M9 8V7a3 3 0 0 1 6 0v1"/>'),
  saude: svg('<path d="M12 20s-7.5-4.6-9-9.5A4.8 4.8 0 0 1 12 7a4.8 4.8 0 0 1 9 3.5C19.5 15.4 12 20 12 20z"/><path d="M8.5 12h2l1-2 1.5 4 1-2h1.5"/>'),
  educacao: svg('<path d="m2 9 10-5 10 5-10 5z"/><path d="M6 11v5c3 2.2 9 2.2 12 0v-5M22 9v6"/>'),
};
const PLUS = svg('<path d="M12 5v14M5 12h14"/>');
const sectorIcon = (id) => icon(SECTOR_ICONS[id] ?? SECTOR_ICONS.geral);

let catalog = null;
const details = new Map();

/** Catálogo de modelos; se não vier, a galeria mostra só o "em branco". */
async function loadCatalog() {
  if (catalog) return catalog;
  try { catalog = await api('/templates/starters'); } catch { return { categories: [], starters: [] }; }
  return catalog;
}

/** Texto, variáveis e signatários de um modelo, guardados para ir e voltar sem nova requisição. */
function loadDetail(id) {
  if (!details.has(id)) {
    details.set(id, api(`/templates/starters/${encodeURIComponent(id)}`).catch((err) => { details.delete(id); throw err; }));
  }
  return details.get(id);
}

/** Sem acento e em minúsculas: "locacao" acha "Locação". */
const fold = (s) => s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase('pt-BR');

/** ["Contratante", "Contratada", "Testemunha", "Testemunha"] → "Contratante, Contratada e 2 testemunhas". */
function signersText(roles) {
  const counts = new Map();
  for (const r of roles) counts.set(r, (counts.get(r) ?? 0) + 1);
  const parts = [...counts].map(([role, n]) => (n > 1 ? `${n} ${role.toLocaleLowerCase('pt-BR')}s` : role));
  return parts.length > 1 ? `${parts.slice(0, -1).join(', ')} e ${parts.at(-1)}` : (parts[0] ?? '');
}

const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

const STEP_TEXT = {
  pick: 'Escolha um modelo pronto do seu setor ou comece do zero.',
  starter: 'Confira o modelo e dê um nome ao template. No editor você ajusta o texto, os campos e quem assina.',
  blank: 'Dê um nome ao template. No editor você escreve ou cola o texto e marca as partes que mudam.',
};

export async function openNewTemplate() {
  const { categories, starters } = await loadCatalog();
  const sectorName = Object.fromEntries(categories.map((c) => [c.id, c.name]));
  let category = 'all';
  let step = 'pick';
  let suggestedName = '';

  const alertEl = h('div', { class: 'alert alert--error', role: 'alert', hidden: true });

  // ---------- Passo 1: galeria ----------
  const search = h('input', {
    class: 'input', type: 'search', 'aria-label': 'Buscar modelo', autocomplete: 'off',
    placeholder: 'Buscar: locação, procuração, estágio, honorários…',
  });

  const counts = { all: starters.length };
  for (const s of starters) counts[s.category] = (counts[s.category] ?? 0) + 1;
  const sectors = [{ id: 'all', name: 'Todos', description: 'Todos os modelos prontos, de todos os setores.' },
    ...categories.filter((c) => counts[c.id])];
  const sectorButtons = sectors.map((c) => h('button', {
    type: 'button', class: 'starter-sectors__item', 'aria-pressed': String(c.id === category), dataset: { sector: c.id },
    onclick: () => { category = c.id; applyFilter(); },
  }, sectorIcon(c.id), h('span', { class: 'starter-sectors__name' }, c.name), h('span', { class: 'starter-sectors__count' }, String(counts[c.id]))));

  const blankCard = h('label', { class: 'starter-card starter-card--blank', dataset: { id: '' } },
    h('input', { type: 'radio', name: 'starter', value: '', checked: true, class: 'visually-hidden', 'aria-labelledby': 'st-blank-t', 'aria-describedby': 'st-blank-d' }),
    h('span', { class: 'starter-card__icon' }, icon(PLUS)),
    h('strong', { class: 'starter-card__title', id: 'st-blank-t' }, 'Em branco'),
    h('span', { class: 'starter-card__desc', id: 'st-blank-d' }, 'Escreva ou cole o seu próprio texto e marque as partes que mudam.'));

  const cards = starters.map((s) => h('label', {
    class: 'starter-card',
    dataset: { id: s.id, sector: s.category, search: fold(`${s.name} ${s.description} ${sectorName[s.category] ?? ''}`) },
  },
  h('input', { type: 'radio', name: 'starter', value: s.id, class: 'visually-hidden', 'aria-labelledby': `st-${s.id}-t`, 'aria-describedby': `st-${s.id}-d` }),
  h('span', { class: 'starter-card__sector' }, sectorIcon(s.category), sectorName[s.category] ?? ''),
  h('strong', { class: 'starter-card__title', id: `st-${s.id}-t` }, s.name),
  h('span', { class: 'starter-card__desc', id: `st-${s.id}-d` }, s.description),
  h('span', { class: 'starter-card__meta' },
    h('span', {}, `Assinam: ${signersText(s.signers)}`),
    h('span', {}, plural(s.variableCount, 'campo', 'campos')))));

  const heading = h('h3', { class: 'starter-picker__title' });
  const headingNote = h('p', { class: 'starter-picker__note' });
  const empty = h('div', { class: 'starter-empty', hidden: true },
    h('p', {}, 'Nenhum modelo encontrado. Tente outra palavra ou comece do zero.'),
    h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => { choose(''); goNext(); } }, 'Começar em branco'));
  const grid = h('div', { class: 'starter-grid', role: 'radiogroup', 'aria-label': 'Modelos' }, blankCard, cards);

  const pickView = h('div', { class: 'starter-picker' },
    h('nav', { class: 'starter-sectors', 'aria-label': 'Setores' }, sectorButtons),
    h('div', { class: 'starter-picker__main' },
      h('div', { class: 'starter-picker__search' }, search),
      h('div', { class: 'starter-picker__scroll' },
        h('div', { class: 'starter-picker__heading' }, heading, headingNote),
        grid, empty)));

  function applyFilter() {
    const words = fold(search.value.trim()).split(/\s+/).filter(Boolean);
    let shown = 0;
    for (const c of cards) {
      const ok = (category === 'all' || c.dataset.sector === category) && words.every((w) => c.dataset.search.includes(w));
      c.hidden = !ok;
      if (ok) shown++;
    }
    blankCard.hidden = words.length > 0 || category !== 'all';
    empty.hidden = shown > 0 || !blankCard.hidden;
    for (const b of sectorButtons) b.setAttribute('aria-pressed', String(b.dataset.sector === category));
    const sector = sectors.find((c) => c.id === category);
    heading.textContent = words.length ? `Resultados para “${search.value.trim()}”` : sector.name === 'Todos' ? 'Todos os modelos' : sector.name;
    headingNote.textContent = words.length
      ? `${plural(shown, 'modelo', 'modelos')}${category === 'all' ? '' : ` em ${sector.name}`}.`
      : sector.description;
  }

  search.addEventListener('input', () => {
    // Buscar procura em todos os setores: é mais fácil achar "procuração" sem saber onde ela mora.
    if (search.value.trim() && category !== 'all') category = 'all';
    applyFilter();
  });
  search.addEventListener('keydown', (e) => { if (e.key === 'Enter') e.preventDefault(); });

  // ---------- Passo 2: conferir e dar nome ----------
  const nameInput = h('input', { class: 'input', id: 't-name', name: 'name', maxlength: 120, required: true, placeholder: 'Ex.: Prestação de serviços' });
  const formFields = h('div', { class: 'starter-review__fields' },
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 't-name' }, 'Nome'),
      nameInput,
      h('p', { class: 'field__error', 'data-error-for': 'name' })),
    h('div', { class: 'field' },
      h('label', { class: 'field__label', for: 't-desc' }, 'Descrição ', h('span', { class: 'muted' }, '(opcional)')),
      h('input', { class: 'input', id: 't-desc', name: 'description', maxlength: 500, placeholder: 'Para quando usar este template' }),
      h('p', { class: 'field__error', 'data-error-for': 'description' })),
    h('p', { class: 'field__error', 'data-error-for': 'starterId' }));
  const reviewView = h('div', { class: 'starter-review', hidden: true });

  function renderReview(detail) {
    if (!detail) {
      clear(reviewView).append(h('div', { class: 'starter-review__blank' }, formFields));
      return;
    }
    // Corpo já sanitizado pelo servidor (texto fixo dos modelos, não dado de usuário).
    const parsed = new DOMParser().parseFromString(detail.previewHtml, 'text/html');
    const sheet = h('article', { class: 'doc-sheet', 'aria-label': `Pré-visualização: ${detail.name}` });
    sheet.append(...parsed.body.childNodes);

    const roles = [...detail.defaultSignerRoles].sort((a, b) => a.order - b.order);
    const required = detail.variables.filter((v) => v.required).length;
    clear(reviewView).append(
      h('div', { class: 'starter-review__doc' },
        h('p', { class: 'starter-review__legend' }, h('span', { class: 'var' }, 'Destacado'), ' = campo preenchido em cada contrato'),
        sheet),
      h('aside', { class: 'starter-review__side' },
        h('div', {},
          h('span', { class: 'starter-card__sector' }, sectorIcon(detail.category), sectorName[detail.category] ?? ''),
          h('h3', { class: 'starter-review__title' }, detail.name),
          h('p', { class: 'starter-review__desc' }, detail.description)),
        h('div', { class: 'starter-facts' },
          h('h4', {}, 'Quem assina'),
          h('ol', { class: 'starter-signers' }, roles.map((r) => h('li', {},
            r.role, r.action === 'SignAsWitness' && !/testemunha/i.test(r.role) ? h('span', { class: 'starter-tag' }, 'testemunha') : null)))),
        h('details', { class: 'starter-facts' },
          h('summary', {}, `${plural(detail.variables.length, 'campo', 'campos')} para preencher`,
            required < detail.variables.length ? h('span', { class: 'muted' }, ` · ${detail.variables.length - required} opcionais`) : null),
          h('ul', { class: 'starter-fields' }, detail.variables.map((v) => h('li', {},
            v.label, v.required ? null : h('span', { class: 'starter-tag' }, 'opcional'))))),
        formFields,
        h('p', { class: 'field__hint' }, 'Os modelos são um ponto de partida: revise o texto com o seu jurídico antes de publicar.')));
  }

  // ---------- Modal ----------
  const form = h('form', { class: 'form starter-form', id: 'new-template', novalidate: true }, alertEl, pickView, reviewView);
  const secondary = h('button', { type: 'button', class: 'btn btn--secondary' }, 'Cancelar');
  const primary = h('button', { type: 'submit', class: 'btn btn--primary', form: 'new-template' }, 'Continuar');
  const picked = h('span', { class: 'modal__footer-note', 'aria-live': 'polite' });
  const { dialog, close } = openModal({
    title: 'Novo template',
    description: STEP_TEXT.pick,
    className: 'modal--wide',
    body: form,
    actions: [picked, secondary, primary],
  });
  const descriptionEl = dialog.querySelector('.modal__header p');

  const selected = () => form.querySelector('input[name="starter"]:checked')?.value ?? '';
  function choose(id) {
    const input = form.querySelector(`input[name="starter"][value="${CSS.escape(id)}"]`);
    if (input) input.checked = true;
    updatePicked();
  }
  function updatePicked() {
    const id = selected();
    picked.textContent = id ? `Selecionado: ${starters.find((s) => s.id === id)?.name ?? ''}` : 'Selecionado: Em branco';
    if (id) loadDetail(id).catch(() => {}); // já vai buscando, para o "Continuar" abrir na hora
  }

  function show(next) {
    step = next;
    const picking = step === 'pick';
    pickView.hidden = !picking;
    reviewView.hidden = picking;
    picked.hidden = !picking;
    dialog.classList.toggle('modal--wide', step !== 'blank');
    descriptionEl.textContent = STEP_TEXT[step];
    secondary.textContent = picking ? 'Cancelar' : 'Voltar';
    primary.textContent = picking ? 'Continuar' : 'Criar e editar';
    clearErrors(form);
    showAlert(alertEl, '');
  }

  async function goNext() {
    const id = selected();
    const starter = starters.find((s) => s.id === id);
    let detail = null;
    if (starter) {
      setBusy(primary, true);
      try { detail = await loadDetail(id); } catch (err) {
        showAlert(alertEl, err.message || 'Não foi possível abrir o modelo agora.');
        return;
      } finally { setBusy(primary, false); }
    }
    // Sugere o nome do modelo, sem apagar o que a pessoa já digitou.
    const next = starter?.name ?? '';
    if (!nameInput.value.trim() || nameInput.value.trim() === suggestedName) nameInput.value = next;
    suggestedName = next;
    renderReview(detail);
    show(starter ? 'starter' : 'blank');
    reviewView.querySelector('.starter-review__doc')?.scrollTo(0, 0);
    // No celular o teclado cobriria a pré-visualização: lá o foco fica no topo.
    if (!detail || matchMedia('(min-width: 720px)').matches) {
      nameInput.focus();
      if (nameInput.value) nameInput.select();
    } else {
      dialog.querySelector('.modal__body').scrollTo(0, 0);
    }
  }

  async function create() {
    clearErrors(form);
    const name = nameInput.value.trim();
    if (name.length < 2) { setFieldError(form, 'name', 'Informe o nome do template.'); nameInput.focus(); return; }
    setBusy(primary, true);
    try {
      const t = await api('/templates', {
        method: 'POST',
        body: { name, description: form.elements.description.value.trim() || null, starterId: selected() || null },
      });
      notify('template.created', { template: { id: t.id, name: t.name } });
      location.assign(href(`/template-editor.html?id=${t.id}`));
    } catch (err) {
      setBusy(primary, false);
      if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
    }
  }

  grid.addEventListener('change', updatePicked);
  grid.addEventListener('dblclick', (e) => { if (e.target.closest('.starter-card')) goNext(); });
  secondary.addEventListener('click', () => {
    if (step === 'pick') close();
    else { show('pick'); form.querySelector('input[name="starter"]:checked')?.focus(); }
  });
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    if (step === 'pick') goNext(); else create();
  });

  applyFilter();
  updatePicked();
  search.focus();
}
