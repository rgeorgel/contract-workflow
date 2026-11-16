/**
 * CSV de planilha (Excel em português salva com ";" e, às vezes, em Windows-1252).
 * Sem dependências: aspas, aspas dobradas, quebra de linha dentro de aspas, CRLF e BOM.
 */

/** Lê o arquivo como UTF-8; se não for UTF-8 válido, como Windows-1252 (o padrão do Excel no Brasil). */
export async function readCsvFile(file) {
  const bytes = new Uint8Array(await file.arrayBuffer());
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return new TextDecoder('windows-1252').decode(bytes);
  }
}

/** Separador mais provável, olhando a primeira linha (fora de aspas). */
function detectDelimiter(text) {
  const firstLine = text.split(/\r?\n/, 1)[0] ?? '';
  const count = (ch) => firstLine.split(ch).length - 1;
  return count(';') >= count(',') ? ';' : ',';
}

/** Matriz de células (sem linhas totalmente vazias). */
export function parseCsv(input) {
  const text = input.replace(/^﻿/, '');
  const delimiter = detectDelimiter(text);
  const rows = [];
  let row = [];
  let cell = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"' && cell === '') {
      quoted = true;
    } else if (ch === delimiter) {
      row.push(cell); cell = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      rows.push(row); row = [];
    } else {
      cell += ch;
    }
  }
  if (cell !== '' || row.length) { row.push(cell); rows.push(row); }
  return rows.filter((r) => r.some((c) => c.trim() !== ''));
}

/** CSV para abrir direto no Excel em português: BOM, ";" e CRLF. */
export function buildCsv(rows) {
  const esc = (v) => {
    const s = String(v ?? '');
    return /[";\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  return '﻿' + rows.map((r) => r.map(esc).join(';')).join('\r\n') + '\r\n';
}

export function download(filename, content, type = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const a = Object.assign(document.createElement('a'), { href: url, download: filename });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
