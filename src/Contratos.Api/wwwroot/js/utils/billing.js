import { DELIVERY } from './status.js';

const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const dayMonth = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'America/Sao_Paulo' });

export const formatMoney = (value) => money.format(value);

/** Último dia do ciclo (o fim é exclusivo: meia-noite do dia seguinte). */
export const periodEndLabel = (iso) => dayMonth.format(new Date(new Date(iso).getTime() - 1));
/** Dia em que a cota renova. */
export const renewsLabel = (iso) => dayMonth.format(new Date(iso));

export const limitLabel = (n, one, many) => (n == null ? `${many} ilimitados` : `${n} ${n === 1 ? one : many}`);

export const channelsLabel = (channels) => channels.map((c) => DELIVERY[c] ?? c).join(', ');

/** Plano mais barato do catálogo que libera o canal. */
export const cheapestWith = (plans, channel) => plans.find((p) => p.channels.includes(channel));

/** 0–100 inteiro, para o data-pct das barras (sem style inline por causa da CSP). */
export const pct = (used, limit) => (limit ? Math.min(100, Math.round((used / limit) * 100)) : 0);
