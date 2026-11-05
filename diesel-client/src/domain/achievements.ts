import type { AchievementMetric, AchievementRule } from '../types';

export const METRIC_LABELS: Record<AchievementMetric, string> = {
  visits: 'Визиты по чекам',
  referrals: 'Приведённые друзья',
  gifts: 'Полученные подарки',
  podium: 'Место в гонке (Топ-N)',
};

export const MAX_ACHIEVEMENTS = 20;

/** Ключ нового достижения: уникальный, латиница и «_» (как требует сервер). */
export function newAchievementKey(existing: readonly AchievementRule[]): string {
  const used = new Set(existing.map((r) => r.key));
  for (let n = existing.length + 1; ; n++) {
    const key = `custom_${n}`;
    if (!used.has(key)) return key;
  }
}

export function emptyAchievement(existing: readonly AchievementRule[]): AchievementRule {
  return { key: newAchievementKey(existing), icon: 'trophy', title: '', description: '', metric: 'visits', target: 1, enabled: true };
}

/** Те же проверки, что на сервере: ошибки видны до сохранения. */
export function validateAchievements(rules: readonly AchievementRule[]): string | null {
  if (rules.length > MAX_ACHIEVEMENTS) return `Достижений — не больше ${MAX_ACHIEVEMENTS}.`;
  for (const r of rules) {
    if (!r.title.trim() || r.title.trim().length > 40) return 'Название достижения: от 1 до 40 символов.';
    if (!r.description.trim() || r.description.trim().length > 100) return 'Условие достижения: от 1 до 100 символов.';
    const max = r.metric === 'podium' ? 50 : 1000;
    if (!Number.isInteger(r.target) || r.target < 1 || r.target > max) return r.metric === 'podium' ? 'Место для пьедестала — от 1 до 50.' : 'Цель достижения — от 1 до 1000.';
  }
  return null;
}
