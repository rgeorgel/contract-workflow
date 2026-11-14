// Editor visual do texto do contrato: contenteditable + barra de ferramentas, sem dependências.
//
// O HTML continua sendo o formato salvo. Aqui ele só é mostrado como documento:
// - {{chave}} vira uma etiqueta (span.var-chip, não editável) e volta a ser {{chave}} ao serializar;
// - atributos style viram data-cw-style ao carregar e voltam ao serializar. A CSP da página
//   (style-src 'self') bloquearia style inline; assim nada é perdido nem gera erro de CSP;
// - o que é colado (Word, Google Docs, sites) passa por uma lista branca de tags.
// O servidor sanitiza de novo ao salvar.

import { h, icon } from './dom.js';

const KEY_SOURCE = '[a-zA-Z][a-zA-Z0-9_]*(?:\\.[a-zA-Z][a-zA-Z0-9_]*)*';
const PLACEHOLDER = new RegExp(`\\{\\{\\s*(${KEY_SOURCE})\\s*\\}\\}`, 'g');
const PLACEHOLDER_AT_END = new RegExp(`\\{\\{\\s*(${KEY_SOURCE})\\s*\\}\\}$`);

const BLOCK_STYLES = { p: 'Parágrafo', h1: 'Título', h2: 'Seção', h3: 'Subseção' };

/** Lista branca do que é colado: tag de origem → tag resultante. */
const PASTE_TAGS = {
  P: 'p', H1: 'h1', H2: 'h2', H3: 'h3', H4: 'h3', H5: 'h3', H6: 'h3',
  STRONG: 'strong', B: 'strong', EM: 'em', I: 'em', U: 'u', SUB: 'sub', SUP: 'sup', BR: 'br', A: 'a',
  UL: 'ul', OL: 'ol', LI: 'li', TABLE: 'table', THEAD: 'thead', TBODY: 'tbody', TFOOT: 'tbody', TR: 'tr', TD: 'td', TH: 'th',
  BLOCKQUOTE: 'p',
};
const BLOCK_TAGS = new Set(['P', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'UL', 'OL', 'LI', 'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TD', 'TH', 'DIV', 'BLOCKQUOTE', 'SECTION', 'ARTICLE', 'HEADER', 'FOOTER']);
const DROP_TAGS = new Set(['SCRIPT', 'STYLE', 'META', 'LINK', 'TITLE', 'HEAD', 'XML', 'OBJECT', 'EMBED', 'IFRAME', 'FRAME', 'SVG', 'TEMPLATE', 'NOSCRIPT', 'BUTTON', 'INPUT', 'SELECT', 'TEXTAREA', 'FORM', 'CANVAS', 'VIDEO', 'AUDIO']);

const ICONS = {
  ul: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M9 6h11M9 12h11M9 18h11"/><circle cx="4.5" cy="6" r="1" fill="currentColor"/><circle cx="4.5" cy="12" r="1" fill="currentColor"/><circle cx="4.5" cy="18" r="1" fill="currentColor"/></svg>',
  ol: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M10 6h10M10 12h10M10 18h10"/><path d="M4 4.5h1.5V9M3.5 9h3M3.5 14.5c.4-.6 1-1 1.7-1 .8 0 1.3.5 1.3 1.1 0 1.2-3 2.1-3 3.4h3" stroke-width="1.5"/></svg>',
  table: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="3.5" y="4.5" width="17" height="15" rx="1.5"/><path d="M3.5 9.5h17M3.5 14.5h17M9.5 9.5v10M15 9.5v10"/></svg>',
  undo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/></svg>',
  redo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 14 5-5-5-5"/><path d="M20 9H9.5a5.5 5.5 0 0 0 0 11H13"/></svg>',
  clear: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 5h12M12 5l-3 14M4 20l16-16" /></svg>',
};

const escapeHtml = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** Tira o que a CSP da página bloquearia ao interpretar o HTML (style inline) e comentários. */
function neutralize(html) {
  return html
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<style\b[\s\S]*?<\/style\s*>/gi, '')
    .replace(/<[a-zA-Z][^>]*>/g, (tag) => tag.replace(/(\s)style(\s*=)/gi, '$1data-cw-style$2'));
}

/** HTML → fragmento inerte (nada carrega nem executa até ir para o documento). */
function parse(html) {
  const tpl = document.createElement('template');
  tpl.innerHTML = neutralize(html);
  return tpl.content;
}

/** Ao carregar HTML salvo: remove o que poderia executar no editor. O resto fica intacto. */
function disarm(root) {
  for (const el of root.querySelectorAll('*')) {
    if (DROP_TAGS.has(el.tagName.toUpperCase())) { el.remove(); continue; }
    for (const attr of [...el.attributes]) {
      const name = attr.name.toLowerCase();
      if (name.startsWith('on') || name === 'contenteditable' || name === 'srcdoc') el.removeAttribute(attr.name);
      else if ((name === 'href' || name === 'src') && /^\s*javascript:/i.test(attr.value)) el.removeAttribute(attr.name);
    }
  }
}

export class RichEditor {
  /**
   * @param {HTMLElement} host onde montar
   * @param {{label: string, placeholder?: string, onChange: (html: string) => void}} options
   */
  constructor(host, { label, placeholder = '', onChange }) {
    this.onChange = onChange;
    this.labels = new Map();
    this.lastRange = null;

    this.area = h('div', {
      class: 'doc-editor', contenteditable: 'true', role: 'textbox', 'aria-multiline': 'true',
      'aria-label': label, 'data-placeholder': placeholder, spellcheck: 'true',
    });
    this.toolbar = this.#buildToolbar();
    host.replaceChildren(h('div', { class: 'rich-editor' }, this.toolbar, this.area));

    this.area.addEventListener('input', (e) => this.#onInput(e));
    this.area.addEventListener('paste', (e) => this.#onPaste(e));
    this.area.addEventListener('drop', (e) => this.#onDrop(e));
    this.area.addEventListener('focus', () => document.execCommand('defaultParagraphSeparator', false, 'p'));
    this.area.addEventListener('keydown', (e) => this.#onKeydown(e));
    document.addEventListener('selectionchange', () => this.#onSelectionChange());
  }

  // ---------- API ----------

  /** Mostra o HTML salvo no editor (não dispara onChange). */
  setHtml(html) {
    const fragment = parse(html ?? '');
    disarm(fragment);
    this.#chipify(fragment);
    this.area.replaceChildren(fragment);
    if (!this.area.firstChild) this.area.append(h('p', {}, h('br')));
    this.lastRange = null;
    this.#updateEmpty();
  }

  /** HTML do documento, com {{chaves}} no lugar das etiquetas. */
  getHtml() {
    const clone = this.area.cloneNode(true);
    for (const chip of clone.querySelectorAll('.var-chip')) {
      const text = document.createTextNode(`{{${chip.dataset.var}}}`);
      // Negrito aplicado à etiqueta (o Chrome põe em style) vira <strong> em volta da {{chave}}.
      if (/bold|[6-9]00/.test(chip.style.fontWeight) && !chip.closest('strong, b')) {
        const strong = document.createElement('strong');
        strong.append(text);
        chip.replaceWith(strong);
      } else {
        chip.replaceWith(text);
      }
    }
    normalize(clone);
    if (!clone.textContent.trim() && !clone.querySelector('img, table, hr')) return '';
    return clone.innerHTML
      .replace(/<[a-zA-Z][^>]*>/g, (tag) => tag.replace(/\sdata-cw-style="/g, ' style="'))
      .replace(/(<\/(?:p|h1|h2|h3|ul|ol|table)>)(?!\n)/g, '$1\n')
      .trim();
  }

  /** Rótulos das variáveis cadastradas; etiquetas de chaves fora da lista ficam destacadas. */
  setVariables(variables) {
    this.labels = new Map(variables.filter((v) => v.key).map((v) => [v.key, v.label || v.key]));
    for (const chip of this.area.querySelectorAll('.var-chip')) this.#paintChip(chip);
  }

  insertVariable(key) {
    this.#restoreSelection();
    const sel = document.getSelection();
    const range = sel.getRangeAt(0);
    range.deleteContents();
    const chip = this.#makeChip(key);
    range.insertNode(chip);
    this.#caretAfter(chip);
    this.#emit();
  }

  setEditable(editable) {
    this.area.setAttribute('contenteditable', editable ? 'true' : 'false');
    this.toolbar.hidden = !editable;
  }

  focus() { this.area.focus(); }

  // ---------- Barra de ferramentas ----------

  #buildToolbar() {
    const keep = (e) => e.preventDefault(); // o clique não tira a seleção do texto
    const button = (labelText, content, onclick, extra = {}) => h('button', {
      type: 'button', class: 'rte-btn', 'aria-label': labelText, title: labelText, onmousedown: keep, onclick, ...extra,
    }, content);
    const cmd = (name, value = null) => () => { this.area.focus(); document.execCommand(name, false, value); this.#emit(); };

    this.blockSelect = h('select', { class: 'select rte-block', 'aria-label': 'Estilo do parágrafo' },
      Object.entries(BLOCK_STYLES).map(([tag, text]) => h('option', { value: tag }, text)));
    this.blockSelect.addEventListener('change', () => {
      this.#restoreSelection();
      document.execCommand('formatBlock', false, this.blockSelect.value);
      this.#emit();
    });

    this.stateButtons = {
      bold: button('Negrito (Ctrl+B)', h('strong', {}, 'N'), cmd('bold'), { 'aria-pressed': 'false' }),
      italic: button('Itálico (Ctrl+I)', h('em', {}, 'I'), cmd('italic'), { 'aria-pressed': 'false' }),
      underline: button('Sublinhado (Ctrl+U)', h('u', {}, 'S'), cmd('underline'), { 'aria-pressed': 'false' }),
      insertUnorderedList: button('Lista com marcadores', icon(ICONS.ul), cmd('insertUnorderedList'), { 'aria-pressed': 'false' }),
      insertOrderedList: button('Lista numerada', icon(ICONS.ol), cmd('insertOrderedList'), { 'aria-pressed': 'false' }),
    };

    this.tableTools = h('div', { class: 'rte-group rte-table-tools', role: 'group', 'aria-label': 'Tabela', hidden: true },
      button('Adicionar linha abaixo', '+ Linha', () => this.#tableOp('addRow'), { class: 'rte-btn rte-btn--text' }),
      button('Adicionar coluna à direita', '+ Coluna', () => this.#tableOp('addCol'), { class: 'rte-btn rte-btn--text' }),
      button('Remover linha', '− Linha', () => this.#tableOp('delRow'), { class: 'rte-btn rte-btn--text' }),
      button('Remover coluna', '− Coluna', () => this.#tableOp('delCol'), { class: 'rte-btn rte-btn--text' }),
      button('Remover tabela', 'Remover tabela', () => this.#tableOp('delTable'), { class: 'rte-btn rte-btn--text rte-btn--danger' }));

    return h('div', { class: 'rte-toolbar', role: 'toolbar', 'aria-label': 'Formatação do texto' },
      h('div', { class: 'rte-group' }, this.blockSelect),
      h('div', { class: 'rte-group' }, this.stateButtons.bold, this.stateButtons.italic, this.stateButtons.underline),
      h('div', { class: 'rte-group' }, this.stateButtons.insertUnorderedList, this.stateButtons.insertOrderedList,
        button('Inserir tabela', icon(ICONS.table), () => this.#insertTable())),
      h('div', { class: 'rte-group' },
        button('Limpar formatação', icon(ICONS.clear), cmd('removeFormat')),
        button('Desfazer (Ctrl+Z)', icon(ICONS.undo), cmd('undo')),
        button('Refazer (Ctrl+Shift+Z)', icon(ICONS.redo), cmd('redo'))),
      this.tableTools);
  }

  #onSelectionChange() {
    const sel = document.getSelection();
    if (!sel?.rangeCount || !this.area.contains(sel.anchorNode)) return;
    this.lastRange = sel.getRangeAt(0).cloneRange();

    for (const [name, btn] of Object.entries(this.stateButtons)) {
      let on = false;
      try { on = document.queryCommandState(name); } catch { /* comando sem estado */ }
      btn.setAttribute('aria-pressed', String(on));
    }
    const block = this.#closest(sel.anchorNode, 'p, h1, h2, h3, h4, h5, h6, li, td, th');
    const tag = block?.tagName.toLowerCase();
    this.blockSelect.value = tag in BLOCK_STYLES ? tag : 'p';
    this.tableTools.hidden = !this.#closest(sel.anchorNode, 'td, th');
  }

  #restoreSelection() {
    this.area.focus();
    const sel = document.getSelection();
    if (this.lastRange && this.area.contains(this.lastRange.startContainer)) {
      sel.removeAllRanges();
      sel.addRange(this.lastRange);
    } else if (!sel.rangeCount || !this.area.contains(sel.anchorNode)) {
      // Sem posição conhecida: no fim do texto.
      const range = document.createRange();
      range.selectNodeContents(this.area.lastElementChild ?? this.area);
      range.collapse(false);
      sel.removeAllRanges();
      sel.addRange(range);
    }
  }

  #closest(node, selector) {
    const el = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    const found = el?.closest(selector);
    return found && this.area.contains(found) ? found : null;
  }

  // ---------- Tabelas ----------

  #insertTable() {
    const cell = '<td><br></td>';
    const row = `<tr>${cell.repeat(2)}</tr>`;
    this.#restoreSelection();
    document.execCommand('insertHTML', false, `<table><tbody>${row.repeat(2)}</tbody></table><p><br></p>`);
    this.#emit();
  }

  #tableOp(op) {
    const sel = document.getSelection();
    const cell = this.#closest(sel?.anchorNode, 'td, th');
    if (!cell) return;
    const row = cell.parentElement;
    const table = cell.closest('table');
    const index = cell.cellIndex;
    const newCell = (like) => { const c = document.createElement(like?.tagName === 'TH' ? 'th' : 'td'); c.append(document.createElement('br')); return c; };

    if (op === 'addRow') {
      const tr = document.createElement('tr');
      for (const c of row.cells) tr.append(newCell(c));
      row.after(tr);
    } else if (op === 'addCol') {
      for (const r of table.rows) r.cells[Math.min(index, r.cells.length - 1)]?.after(newCell(r.cells[index]));
    } else if (op === 'delRow') {
      if (table.rows.length <= 1) table.remove(); else row.remove();
    } else if (op === 'delCol') {
      if (row.cells.length <= 1) table.remove();
      else for (const r of table.rows) r.cells[index]?.remove();
    } else if (op === 'delTable') {
      table.remove();
    }
    if (!this.area.firstChild) this.area.append(h('p', {}, h('br')));
    this.tableTools.hidden = !this.area.contains(cell);
    this.#emit();
  }

  // ---------- Edição ----------

  #onKeydown(e) {
    // Tab dentro da tabela anda entre células em vez de sair do editor.
    if (e.key !== 'Tab') return;
    const cell = this.#closest(document.getSelection()?.anchorNode, 'td, th');
    if (!cell) return;
    const cells = [...cell.closest('table').querySelectorAll('td, th')];
    const next = cells[cells.indexOf(cell) + (e.shiftKey ? -1 : 1)];
    if (!next) return;
    e.preventDefault();
    const range = document.createRange();
    range.selectNodeContents(next);
    range.collapse(true);
    const sel = document.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
  }

  #onInput(e) {
    // Fechou um {{chave}} digitando: vira etiqueta.
    if (e.inputType === 'insertText' && e.data?.includes('}')) this.#chipifyAtCaret();
    this.#emit();
  }

  #chipifyAtCaret() {
    const sel = document.getSelection();
    const node = sel?.anchorNode;
    if (!node || node.nodeType !== Node.TEXT_NODE || !sel.isCollapsed) return;
    if (!PLACEHOLDER_AT_END.test(node.data.slice(0, sel.anchorOffset))) return;
    this.#chipifyKeepingCaret();
  }

  /** Converte {{chaves}} do editor em etiquetas sem tirar o cursor do lugar. */
  #chipifyKeepingCaret() {
    const sel = document.getSelection();
    const caret = sel?.rangeCount && sel.isCollapsed && this.area.contains(sel.anchorNode)
      ? { node: sel.anchorNode, offset: sel.anchorOffset } : null;
    const moved = this.#chipify(this.area, caret);
    if (!moved) return;
    const range = document.createRange();
    if (moved.after) range.setStartAfter(moved.after); else range.setStart(moved.node, moved.offset);
    range.collapse(true);
    sel.removeAllRanges();
    sel.addRange(range);
  }

  #caretAfter(node) {
    const range = document.createRange();
    range.setStartAfter(node);
    range.collapse(true);
    const sel = document.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
    this.lastRange = range.cloneRange();
  }

  #onPaste(e) {
    const data = e.clipboardData;
    if (!data) return;
    e.preventDefault();
    this.#insertClipboard(data);
  }

  #onDrop(e) {
    const data = e.dataTransfer;
    if (!data) return;
    e.preventDefault();
    const range = document.caretRangeFromPoint?.(e.clientX, e.clientY);
    if (range && this.area.contains(range.startContainer)) {
      const sel = document.getSelection();
      sel.removeAllRanges();
      sel.addRange(range);
    }
    this.#insertClipboard(data);
  }

  #insertClipboard(data) {
    const html = data.getData('text/html');
    const out = html ? cleanPastedHtml(html) : textToHtml(data.getData('text/plain'));
    // O insertHTML do navegador sabe quebrar parágrafos ao colar blocos; as {{chaves}} viram
    // etiquetas depois (inserir span não editável pelo insertHTML desmonta o parágrafo no Chrome).
    if (out) document.execCommand('insertHTML', false, out);
    this.#chipifyKeepingCaret();
    this.#emit();
  }

  // ---------- Etiquetas de variável ----------

  #makeChip(key) {
    const chip = document.createElement('span');
    chip.className = 'var-chip';
    chip.setAttribute('contenteditable', 'false');
    chip.dataset.var = key;
    this.#paintChip(chip);
    return chip;
  }

  #paintChip(chip) {
    const key = chip.dataset.var;
    const label = this.labels.get(key);
    chip.textContent = label ?? key;
    chip.title = label ? `{{${key}}}` : `{{${key}}}: variável ainda não cadastrada`;
    chip.classList.toggle('var-chip--missing', !label);
  }

  /**
   * Troca {{chave}} em nós de texto por etiquetas. Se `caret` ({node, offset}) estava num nó trocado,
   * devolve a nova posição: {node, offset} num texto, ou {after} logo depois de uma etiqueta.
   */
  #chipify(root, caret = null) {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    const nodes = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (n.data.includes('{{') && !n.parentElement?.closest?.('.var-chip')) nodes.push(n);
    }
    let moved = null;
    for (const node of nodes) {
      const matches = [...node.data.matchAll(PLACEHOLDER)];
      if (!matches.length) continue;
      const parts = []; // [nó, início no texto original, fim]
      let last = 0;
      for (const m of matches) {
        if (m.index > last) parts.push([document.createTextNode(node.data.slice(last, m.index)), last, m.index]);
        parts.push([this.#makeChip(m[1]), m.index, m.index + m[0].length]);
        last = m.index + m[0].length;
      }
      if (last < node.data.length) parts.push([document.createTextNode(node.data.slice(last)), last, node.data.length]);

      if (caret?.node === node) {
        // Cursor dentro de uma {{chave}} trocada fica logo depois da etiqueta.
        const [part, start] = parts.find(([, s, e]) => caret.offset >= s && caret.offset <= e) ?? parts.at(-1);
        moved = part.nodeType === Node.TEXT_NODE ? { node: part, offset: caret.offset - start } : { after: part };
      }
      node.replaceWith(...parts.map(([part]) => part));
    }
    return moved;
  }

  /** Conserta o que o contenteditable desarruma, sem tirar o cursor do lugar. */
  #fixStructure() {
    const sel = document.getSelection();
    const saved = sel?.rangeCount && this.area.contains(sel.anchorNode)
      ? [sel.anchorNode, sel.anchorOffset, sel.focusNode, sel.focusOffset] : null;
    const changed = liftNestedBlocks(this.area) | wrapLooseInline(this.area);
    if (changed && saved?.[0].isConnected && saved[2].isConnected) {
      try { sel.setBaseAndExtent(...saved); } catch { /* posição deixou de existir */ }
    }
  }

  #updateEmpty() {
    const empty = !this.area.textContent.trim() && !this.area.querySelector('.var-chip, img, table');
    this.area.classList.toggle('is-empty', empty);
  }

  #emit() {
    this.#fixStructure();
    this.#updateEmpty();
    this.onChange(this.getHtml());
  }
}

/**
 * O Chrome às vezes põe blocos dentro de parágrafos (lista criada num <p> vira <p><ol>…</ol></p>).
 * Tira o bloco para fora, movendo os nós (não copiando), para o cursor continuar onde estava.
 */
function liftNestedBlocks(root) {
  let changed = false;
  for (const holder of [...root.querySelectorAll('p, h1, h2, h3')]) {
    if (!holder.isConnected || ![...holder.children].some((c) => BLOCK_TAGS.has(c.tagName.toUpperCase()))) continue;
    const out = [];
    let run = null;
    for (const node of [...holder.childNodes]) {
      if (node.nodeType === Node.ELEMENT_NODE && BLOCK_TAGS.has(node.tagName.toUpperCase())) {
        run = null;
        out.push(node);
      } else {
        if (!run) {
          if (node.nodeType === Node.TEXT_NODE && !node.data.trim()) continue;
          run = document.createElement(holder.tagName.toLowerCase());
          out.push(run);
        }
        run.append(node);
      }
    }
    holder.replaceWith(...out);
    changed = true;
  }
  return changed;
}

/** Texto e elementos inline soltos na raiz do editor vão para um <p> (o Chrome deixa <span> ao desfazer listas). */
function wrapLooseInline(root) {
  let changed = false;
  let run = null;
  for (const node of [...root.childNodes]) {
    const isBlock = node.nodeType === Node.ELEMENT_NODE
      && (BLOCK_TAGS.has(node.tagName.toUpperCase()) || node.tagName === 'HR' || node.tagName === 'IMG');
    if (isBlock || node.nodeType === Node.COMMENT_NODE) { run = null; continue; }
    if (node.nodeType === Node.TEXT_NODE && !node.data.trim() && !run) continue;
    if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'SPAN' && !node.classList.contains('var-chip')) {
      // span do navegador: some, fica o conteúdo
      const children = [...node.childNodes];
      if (!run) { run = document.createElement('p'); node.before(run); }
      node.remove();
      run.append(...children);
      changed = true;
      continue;
    }
    if (!run) { run = document.createElement('p'); node.before(run); }
    run.append(node);
    changed = true;
  }
  return changed;
}

/** Colado de fora: só a estrutura (títulos, parágrafos, ênfase, listas, tabelas, links https). */
function cleanPastedHtml(html) {
  const out = document.createElement('div');
  cleanInto(parse(html), out);
  return out.innerHTML;
}

function textToHtml(text) {
  if (!text) return '';
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  if (lines.length === 1) return escapeHtml(lines[0]);
  return lines.map((line) => (line.trim() ? `<p>${escapeHtml(line)}</p>` : '<p><br></p>')).join('');
}

/** Copia `source` para `out` passando pela lista branca de tags. */
function cleanInto(source, out) {
  for (const child of [...source.childNodes]) {
    if (child.nodeType === Node.TEXT_NODE) {
      const text = child.data.replace(/[ \t\r\n]+/g, ' ');
      if (text) out.append(text);
      continue;
    }
    if (child.nodeType !== Node.ELEMENT_NODE) continue;

    const tag = child.tagName.toUpperCase();
    if (DROP_TAGS.has(tag)) continue;
    const style = (child.getAttribute('data-cw-style') ?? '').toLowerCase();

    let target = PASTE_TAGS[tag];
    if (tag === 'DIV') target = [...child.children].some((c) => BLOCK_TAGS.has(c.tagName.toUpperCase())) ? null : 'p';
    // Google Docs embrulha tudo num <b style="font-weight:normal">.
    if ((tag === 'B' || tag === 'STRONG') && /font-weight:\s*(normal|[1-4]00)\b/.test(style)) target = null;
    if (tag === 'A') {
      const href = child.getAttribute('href') ?? '';
      target = /^(https:|mailto:)/i.test(href.trim()) ? 'a' : null;
    }
    if (tag === 'IMG') {
      const src = child.getAttribute('src') ?? '';
      if (/^data:image\/(png|jpeg|gif|webp);base64,/i.test(src)) out.append(h('img', { src, alt: child.getAttribute('alt') ?? '' }));
      continue;
    }

    // Ênfase vinda de CSS (Google Docs, Word) vira tag.
    let container = out;
    if (!target || target === 'p' || target === 'td' || target === 'th') {
      const wrap = [];
      if (/font-weight:\s*(bold|[6-9]00)\b/.test(style) && tag !== 'B' && tag !== 'STRONG') wrap.push('strong');
      if (/font-style:\s*italic/.test(style) && tag !== 'I' && tag !== 'EM') wrap.push('em');
      if (/text-decoration[^;]*underline/.test(style) && tag !== 'U') wrap.push('u');
      if (wrap.length && !target) {
        for (const t of wrap) { const el = document.createElement(t); container.append(el); container = el; }
        cleanInto(child, container);
        continue;
      }
    }

    if (!target) { cleanInto(child, out); continue; } // desembrulha, fica o conteúdo

    const el = document.createElement(target);
    if (target === 'a') {
      el.setAttribute('href', child.getAttribute('href').trim());
    } else if (target === 'td' || target === 'th') {
      for (const name of ['colspan', 'rowspan']) {
        const v = child.getAttribute(name);
        if (v && /^\d{1,2}$/.test(v)) el.setAttribute(name, v);
      }
    }
    out.append(el);
    if (target !== 'br') cleanInto(child, el);
  }
}

/** Ajustes do que o contenteditable produz: tags equivalentes, div → p, spans vazios, style do navegador. */
function normalize(root) {
  const rename = { B: 'strong', I: 'em', DIV: 'p' };
  for (const el of [...root.querySelectorAll('*')]) {
    el.removeAttribute('contenteditable');
    el.removeAttribute('style'); // style que o navegador inventa (o do usuário está em data-cw-style)
    const tag = el.tagName.toUpperCase();
    if (tag === 'SPAN' && el.attributes.length === 0) { el.replaceWith(...el.childNodes); continue; }
    if (tag === 'FONT') { el.replaceWith(...el.childNodes); continue; }
    if (tag === 'DIV' && [...el.children].some((c) => BLOCK_TAGS.has(c.tagName.toUpperCase()))) { el.replaceWith(...el.childNodes); continue; }
    const to = rename[tag];
    if (to) {
      const repl = document.createElement(to);
      for (const attr of el.attributes) repl.setAttribute(attr.name, attr.value);
      repl.append(...el.childNodes);
      el.replaceWith(repl);
    }
  }

  // Texto solto na raiz (sem parágrafo) vai para um <p>.
  let run = null;
  for (const node of [...root.childNodes]) {
    const inline = node.nodeType === Node.TEXT_NODE || (node.nodeType === Node.ELEMENT_NODE && !BLOCK_TAGS.has(node.tagName.toUpperCase()) && node.tagName !== 'HR' && node.tagName !== 'IMG');
    if (inline) {
      if (node.nodeType === Node.TEXT_NODE && !node.data.trim() && !run) { node.remove(); continue; }
      if (!run) { run = document.createElement('p'); node.before(run); }
      run.append(node);
    } else {
      run = null;
    }
  }
  // Espaço que o navegador troca por &nbsp; ao lado de etiquetas e no fim da linha volta a ser espaço.
  root.normalize();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    if (n.data.includes('\u00a0')) n.data = n.data.replace(/(\S)\u00a0(?=\S)/g, '$1 ');
  }
  // <br> que o navegador deixa no fim de um bloco que já tem texto.
  for (const block of root.querySelectorAll('p, h1, h2, h3, li, td, th')) {
    const last = block.lastChild;
    if (last?.nodeName === 'BR' && block.childNodes.length > 1) last.remove();
  }
  for (const p of root.querySelectorAll(':scope > p')) {
    while (p.lastChild?.nodeType === Node.TEXT_NODE && !p.lastChild.data.trim() && p.childNodes.length > 1) p.lastChild.remove();
  }
}
