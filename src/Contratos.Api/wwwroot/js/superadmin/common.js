// Rótulos, badges e medidores compartilhados pelas páginas do portal super admin.
import { h } from '../ui/dom.js';
import { pct } from '../utils/billing.js';
import { formatRelative } from '../utils/format.js';

// Datas no horário de Brasília (o mesmo dos ciclos e da linha do tempo), seja qual for o fuso do navegador.
const TZ = 'America/Sao_Paulo';
const brDate = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: TZ });
const brDateTime = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: TZ });
const plainDay = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC' });

export const formatDate = (iso) => (iso ? brDate.format(new Date(iso)) : '—');
export const formatDateTime = (iso) => (iso ? brDateTime.format(new Date(iso)) : '—');
/** Ciclo [início, fim): mostra o último dia que vale, não o primeiro do próximo. */
export const cycleLabel = (start, end) => `${formatDate(start)} a ${formatDate(new Date(new Date(end).getTime() - 1))}`;

const svg = (paths) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;

export const SA_ICONS = {
  chart: svg('<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>'),
  building: svg('<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M9 7h1M14 7h1M9 11h1M14 11h1M9 15h1M14 15h1M10 21v-3h4v3"/>'),
  grid: svg('<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>'),
  list: svg('<path d="M8 6h13M8 12h13M8 18h13M3.5 6h.01M3.5 12h.01M3.5 18h.01"/>'),
  user: svg('<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>'),
  doc: svg('<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>'),
  pen: svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'),
  layers: svg('<path d="m12 3 9 5-9 5-9-5Z"/><path d="m3 13 9 5 9-5"/>'),
  coin: svg('<circle cx="12" cy="12" r="9"/><path d="M15 9.5c-.5-1-1.6-1.5-3-1.5-1.7 0-3 .8-3 2s1.3 1.7 3 2 3 .8 3 2-1.3 2-3 2c-1.4 0-2.5-.5-3-1.5M12 6v2M12 16v2"/>'),
  plug: svg('<path d="M9 2v6M15 2v6M6 8h12v4a6 6 0 0 1-12 0Z"/><path d="M12 18v4"/>'),
  shield: svg('<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6Z"/>'),
  all: svg('<circle cx="12" cy="12" r="9"/><path d="M8 12h8"/>'),
};

export const STATUS = {
  active: { label: 'Ativo', badge: 'success', tone: 'success' },
  trialing: { label: 'Trial', badge: 'info', tone: 'info' },
  past_due: { label: 'Pagamento pendente', badge: 'danger', tone: 'danger' },
  manual: { label: 'Plano manual', badge: 'info', tone: 'info' },
  canceled: { label: 'Assinatura encerrada', badge: 'neutral', tone: 'warning' },
  payg: { label: 'Sem assinatura', badge: 'neutral', tone: null },
  suspended: { label: 'Suspenso', badge: 'danger', tone: 'danger' },
};

export const PLAN_NAMES = { avulso: 'Avulso', essencial: 'Essencial', profissional: 'Profissional', empresa: 'Empresa' };

export const statusBadge = (status) => {
  const s = STATUS[status] ?? { label: status, badge: 'neutral' };
  return h('span', { class: `badge badge--${s.badge}` }, s.label);
};

export const planBadge = (planId, name) => h('span', { class: 'plan-badge', dataset: { plan: planId } }, name ?? PLAN_NAMES[planId] ?? planId);

/** "1 contrato" / "3 contratos". */
export const plural = (n, one, many) => `${n.toLocaleString('pt-BR')} ${n === 1 ? one : many}`;

/** Barra de uso: "Usuários 2/3"; sem limite mostra "∞" e barra vazia. */
export function meterRow(label, used, limit, { hint } = {}) {
  const unlimited = limit == null;
  const p = unlimited ? 0 : pct(used, limit);
  const cls = unlimited ? 'is-none' : p >= 100 ? 'is-full' : p >= 80 ? 'is-warn' : '';
  return h('div', { class: 'meter-row', title: hint ?? null },
    h('span', { class: 'meter-row__label' }, h('span', {}, label), h('strong', {}, `${used}/${unlimited ? '∞' : limit}`)),
    h('span', { class: 'mini-progress__track', 'aria-hidden': 'true' },
      h('span', { class: `mini-progress__bar ${cls}`, dataset: { pct: p } })));
}

export const chip = (text, kind, value, title) =>
  h('span', { class: `chip${kind ? ` chip--${kind}` : ''}${value === 0 ? ' is-zero' : ''}`, title: title ?? null }, text);

/** Fichas dos contratos por status. */
export function contractChips(c) {
  return h('span', { class: 'chips' },
    chip(`Abertos ${c.open}`, 'info', c.open, 'Enviados ou parcialmente assinados'),
    chip(`Concl. ${c.completed}`, 'success', c.completed, 'Concluídos'),
    chip(`Recus. ${c.declined}`, 'danger', c.declined, 'Recusados'),
    chip(`Canc. ${c.cancelled + c.expired}`, null, c.cancelled + c.expired, 'Cancelados ou expirados'),
    chip(`Rasc. ${c.draft}`, null, c.draft, 'Rascunhos'));
}

/** "Acesso há 4 horas" com o tom conforme o tempo sem entrar. */
export function accessLine(lastSeenAt) {
  if (!lastSeenAt) return h('span', { class: 'is-never' }, 'Nunca acessou');
  const days = (Date.now() - new Date(lastSeenAt).getTime()) / 86_400_000;
  return h('span', { class: days > 30 ? 'is-stale' : null, title: formatDateTime(lastSeenAt) },
    'Acesso ', h('strong', {}, formatRelative(lastSeenAt)));
}

export const sinceLabel = (iso) => `desde ${formatDate(iso)}`;

export const tenantHref = (id) => `/superadmin/tenant.html?id=${encodeURIComponent(id)}`;

/** Gráfico de barras em SVG (atributos, não style: a CSP bloqueia style inline). */
export function barChart(points, { label }) {
  const NS = 'http://www.w3.org/2000/svg';
  const el = (tag, attrs, ...kids) => {
    const node = document.createElementNS(NS, tag);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, String(v));
    node.append(...kids);
    return node;
  };
  const w = 600, hgt = 120, pad = 0, n = points.length;
  const max = Math.max(1, ...points.map((p) => p.count));
  const bw = (w / n) * 0.7, gap = (w / n) * 0.3;
  const chart = el('svg', { class: 'chart', viewBox: `0 0 ${w} ${hgt}`, preserveAspectRatio: 'none', role: 'img', 'aria-label': label });
  points.forEach((p, i) => {
    const bh = p.count ? Math.max(3, ((hgt - pad) * p.count) / max) : 3;
    const rect = el('rect', { x: i * (bw + gap) + gap / 2, y: hgt - pad - bh, width: bw, height: bh, rx: 2, class: p.count ? '' : 'is-zero' },
      el('title', {}, `${plainDay.format(new Date(`${p.day}T12:00:00Z`))}: ${p.count}`));
    chart.append(rect);
  });
  // Datas fora do SVG: com preserveAspectRatio="none" o texto sairia esticado.
  const dayLabel = (p) => p.day.split('-').reverse().slice(0, 2).join('/');
  return h('div', {}, chart, h('div', { class: 'chart-axis', 'aria-hidden': 'true' }, h('span', {}, dayLabel(points[0])), h('span', {}, dayLabel(points[n - 1]))));
}
