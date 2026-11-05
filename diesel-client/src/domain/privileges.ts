import type { Privilege, PrivilegeTier } from '../types';

// Переходящие привилегии «Гонки недели» (ТЗ п. 4.1) — зеркало серверного PrivilegeCatalog для подписей в интерфейсе.
// icon — ключ векторной иконки (components/Icon.tsx), tone — цвет маркера уровня.
export type TierTone = 'gold' | 'silver' | 'bronze' | 'red' | 'amber' | 'steel' | 'muted';

export interface TierMeta {
  tier: Exclude<PrivilegeTier, 'None'>;
  top: number;
  bottom: number;
  tone: TierTone;
  icon: string;
  title: string;
  conditions: string;
  ageRestricted?: boolean;
}

export const TIERS: TierMeta[] = [
  { tier: 'BusinessLunch', top: 1, bottom: 1, tone: 'gold', icon: 'lunch', title: 'Бесплатный бизнес-ланч', conditions: 'суп, горячее, напиток, хлебная корзина · до 3 раз в неделю, Пн–Пт 12:00–16:00' },
  { tier: 'UnlimitedCoffee', top: 2, bottom: 2, tone: 'silver', icon: 'coffee', title: 'Безлимитный кофе', conditions: 'в любой день, пока вы на 2-м месте' },
  { tier: 'Hookah', top: 3, bottom: 3, tone: 'bronze', icon: 'hookah', title: 'Классический кальян', conditions: '1 раз в неделю, Пт/Сб', ageRestricted: true },
  { tier: 'Dessert', top: 4, bottom: 10, tone: 'red', icon: 'dessert', title: 'Десерт в подарок', conditions: '1 раз в неделю' },
  { tier: 'BeerOrCoffee', top: 11, bottom: 20, tone: 'amber', icon: 'beer', title: 'Бокал разливного пива или кофе', conditions: '1 раз в неделю', ageRestricted: true },
  { tier: 'TeaPot', top: 21, bottom: 30, tone: 'steel', icon: 'tea', title: 'Чайник чая на выбор', conditions: '1 раз в неделю' },
  { tier: 'Cocktail', top: 31, bottom: 50, tone: 'muted', icon: 'cocktail', title: 'Бесплатный коктейль', conditions: '1 раз в 14 дней, на вечеринках Пт/Сб', ageRestricted: true },
];

export const STREAK_TARGET = 7;
export const STREAK_PRIZE = 'Бутылка Хаски 0.5';

export function tierForRank(rank: number | null | undefined): TierMeta | null {
  if (!rank || rank < 1) return null;
  return TIERS.find((t) => rank >= t.top && rank <= t.bottom) ?? null;
}

export function tierMeta(tier: PrivilegeTier): TierMeta | null {
  return TIERS.find((t) => t.tier === tier) ?? null;
}

/** Цвет маркера места: подиум — золото/серебро/бронза, 4–50 — цвет тира, дальше — без маркера. */
export function rankTone(rank: number): TierTone | null {
  return tierForRank(rank)?.tone ?? null;
}

export function tierRangeLabel(t: TierMeta): string {
  return t.top === t.bottom ? `${t.top} место` : `${t.top}–${t.bottom} место`;
}

/** «Удержание 1-го места: X / 7 дней подряд» — только для лидера. */
export function streakText(privilege: Pick<Privilege, 'rank' | 'firstPlaceStreak' | 'streakTarget'>): string | null {
  if (privilege.rank !== 1) return null;
  const target = privilege.streakTarget || STREAK_TARGET;
  return `Удержание 1-го места: ${Math.min(privilege.firstPlaceStreak, target)} / ${target} дней подряд`;
}

/** Подпись использования лимита: «использовано 2 из 3 на этой неделе», «без лимита». */
export function usageText(privilege: Pick<Privilege, 'used' | 'limit' | 'tier'>): string {
  if (privilege.tier === 'None') return '';
  if (privilege.limit == null) return 'без лимита, пока держите место';
  const period = privilege.tier === 'Cocktail' ? 'за 14 дней' : 'на этой неделе';
  return `использовано ${privilege.used} из ${privilege.limit} ${period}`;
}
