import type { ShopOrder } from '../types';
import { parseIso } from '../utils/format';

export type GiftsTab = 'pending' | 'received' | 'closed';

export const GIFT_TABS: { key: GiftsTab; label: string }[] = [
  { key: 'pending', label: 'К получению' },
  { key: 'received', label: 'Полученные' },
  { key: 'closed', label: 'Отменённые / Истёкшие' },
];

/** Купон «К получению», но срок уже вышел — до ответа сервера показываем его как истёкший. */
export function isEffectivelyExpired(order: ShopOrder, nowMs: number): boolean {
  return order.status === 'Pending' && parseIso(order.expiresAt) <= nowMs;
}

export function giftsTabOf(order: ShopOrder, nowMs: number): GiftsTab {
  if (order.status === 'Issued') return 'received';
  if (order.status === 'Pending' && !isEffectivelyExpired(order, nowMs)) return 'pending';
  return 'closed';
}

export function groupOrders(orders: ShopOrder[], nowMs: number): Record<GiftsTab, ShopOrder[]> {
  const result: Record<GiftsTab, ShopOrder[]> = { pending: [], received: [], closed: [] };
  for (const order of orders) result[giftsTabOf(order, nowMs)].push(order);
  // Ближайшие к истечению — сверху
  result.pending.sort((a, b) => parseIso(a.expiresAt) - parseIso(b.expiresAt));
  return result;
}

export function orderStatusLabel(order: ShopOrder, nowMs: number): string {
  if (isEffectivelyExpired(order, nowMs)) return 'Истёк · баллы возвращены';
  switch (order.status) {
    case 'Pending':
      return 'К получению';
    case 'Issued':
      return 'Получен';
    case 'Cancelled':
      return order.priceCoins > 0 ? 'Отменён · баллы возвращены' : 'Отменён';
    case 'Expired':
      return order.priceCoins > 0 ? 'Истёк · баллы возвращены' : 'Истёк';
  }
}

/** «DR-AB3K» → «#DR-AB3K» — так код показывается гостю и кодируется в QR. */
export function displayCode(code: string): string {
  return code.startsWith('#') ? code : `#${code}`;
}

const CODE_RE = /^#?\s*(?:DR)?\s*-?\s*([A-Z0-9]{4})$/i;

/** Ввод официанта («#dr-ab3k», «ab3k») → «DR-AB3K» или null. */
export function normalizeOrderCode(input: string): string | null {
  const match = CODE_RE.exec(input.trim());
  return match ? `DR-${match[1].toUpperCase()}` : null;
}
