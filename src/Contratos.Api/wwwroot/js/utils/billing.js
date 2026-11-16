import { DELIVERY } from './status.js';

const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
// Datas do ciclo em UTC, como o Stripe mostra. O mês do calendário vira à meia-noite de Brasília (03:00 UTC):
// mesmo dia nos dois fusos.
const dayMonth = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'UTC' });

export const formatMoney = (value) => money.format(value);

/** Dia em que a cota renova (ou em que a assinatura cancelada termina). */
export const renewsLabel = (iso) => dayMonth.format(new Date(iso));

export const limitLabel = (n, one, many) => (n == null ? `${many} ilimitados` : `${n} ${n === 1 ? one : many}`);

export const channelsLabel = (channels) => channels.map((c) => DELIVERY[c] ?? c).join(', ');

/** Plano de assinatura mais barato que libera o canal (o Avulso libera todos, mas não é um "upgrade"). */
export const cheapestWith = (plans, channel) =>
  plans.find((p) => p.monthlyPrice > 0 && p.channels.includes(channel)) ?? plans.find((p) => p.channels.includes(channel));

/** 0–100 inteiro, para o data-pct das barras (sem style inline por causa da CSP). */
export const pct = (used, limit) => (limit ? Math.min(100, Math.round((used / limit) * 100)) : 0);
