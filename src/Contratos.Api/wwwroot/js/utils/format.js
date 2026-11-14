const dateFmt = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
const dateTimeFmt = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
const rtf = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' });

export const formatDate = (iso) => (iso ? dateFmt.format(new Date(iso)) : '—');
export const formatDateTime = (iso) => (iso ? dateTimeFmt.format(new Date(iso)) : '—');

/** "há 5 minutos", "ontem", ou a data se for antigo. */
export function formatRelative(iso) {
  if (!iso) return '—';
  const diffMs = new Date(iso).getTime() - Date.now();
  const abs = Math.abs(diffMs);
  const min = 60_000, hour = 60 * min, day = 24 * hour;
  if (abs < min) return 'agora';
  if (abs < hour) return rtf.format(Math.round(diffMs / min), 'minute');
  if (abs < day) return rtf.format(Math.round(diffMs / hour), 'hour');
  if (abs < 7 * day) return rtf.format(Math.round(diffMs / day), 'day');
  return formatDate(iso);
}

export const contractNumber = (n) => String(n).padStart(4, '0');

/** "contratante.nome_completo" → "Contratante nome completo" */
export function humanize(key) {
  const text = key.replace(/[._]+/g, ' ').trim();
  return text.charAt(0).toUpperCase() + text.slice(1);
}
