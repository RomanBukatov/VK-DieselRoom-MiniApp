import type { Privilege, PrivilegeTier } from '../types';

// Переходящие привилегии «Гонки недели» (ТЗ п. 4.1) — зеркало серверного PrivilegeCatalog для подписей в интерфейсе
export interface TierMeta {
  tier: Exclude<PrivilegeTier, 'None'>;
  top: number;
  bottom: number;
  dot: string;
  emoji: string;
  title: string;
  conditions: string;
}

export const TIERS: TierMeta[] = [
  { tier: 'BusinessLunch', top: 1, bottom: 1, dot: '🥇', emoji: '🍱', title: 'Бизнес-ланч бесплатно', conditions: 'до 3 раз в неделю, Пн–Пт 12:00–16:00' },
  { tier: 'UnlimitedCoffee', top: 2, bottom: 2, dot: '🥈', emoji: '☕', title: 'Безлимитный кофе', conditions: 'активен, пока вы на 2-м месте!' },
  { tier: 'Hookah', top: 3, bottom: 3, dot: '🥉', emoji: '💨', title: 'Бесплатный кальян', conditions: '1 раз в неделю, Пт/Сб' },
  { tier: 'Dessert', top: 4, bottom: 10, dot: '🟢', emoji: '🍰', title: 'Десерт от заведения', conditions: '1 раз в неделю' },
  { tier: 'BeerOrCoffee', top: 11, bottom: 20, dot: '🟡', emoji: '🍺', title: 'Бокал пива 0.5 или кофе', conditions: '1 раз в неделю' },
  { tier: 'TeaPot', top: 21, bottom: 30, dot: '🔵', emoji: '🫖', title: 'Чайник чая', conditions: '1 раз в неделю' },
  { tier: 'Cocktail', top: 31, bottom: 50, dot: '⚪', emoji: '🍹', title: 'Коктейль от бара', conditions: '1 раз в 2 недели' },
];

export const STREAK_TARGET = 7;
export const STREAK_PRIZE = '🥃 Бутылка Хаски 0.5';

export function tierForRank(rank: number | null | undefined): TierMeta | null {
  if (!rank || rank < 1) return null;
  return TIERS.find((t) => rank >= t.top && rank <= t.bottom) ?? null;
}

export function tierMeta(tier: PrivilegeTier): TierMeta | null {
  return TIERS.find((t) => t.tier === tier) ?? null;
}

/** Значок места: медали для подиума, цветная точка тира для 4–50. */
export function rankBadge(rank: number): string {
  return tierForRank(rank)?.dot ?? '';
}

export function tierRangeLabel(t: TierMeta): string {
  return t.top === t.bottom ? `${t.top} место` : `${t.top}–${t.bottom} место`;
}

/** «🔥 Удержание 1-го места: X / 7 дней подряд» — только для лидера. */
export function streakText(privilege: Pick<Privilege, 'rank' | 'firstPlaceStreak' | 'streakTarget'>): string | null {
  if (privilege.rank !== 1) return null;
  const target = privilege.streakTarget || STREAK_TARGET;
  return `🔥 Удержание 1-го места: ${Math.min(privilege.firstPlaceStreak, target)} / ${target} дней подряд`;
}

/** Подпись использования лимита: «2 из 3 на этой неделе», «без лимита». */
export function usageText(privilege: Pick<Privilege, 'used' | 'limit' | 'tier'>): string {
  if (privilege.tier === 'None') return '';
  if (privilege.limit == null) return 'без лимита, пока держите место';
  const period = privilege.tier === 'Cocktail' ? 'за 2 недели' : 'на этой неделе';
  return `использовано ${privilege.used} из ${privilege.limit} ${period}`;
}
