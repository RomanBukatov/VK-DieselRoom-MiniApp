import { describe, expect, it } from 'bun:test';
import { emptyAchievement, newAchievementKey, validateAchievements } from './achievements';
import type { AchievementRule } from '../types';

const rule = (patch: Partial<AchievementRule> = {}): AchievementRule => ({
  key: 'a', icon: 'flame', title: 'Завсегдатай', description: '3 визита', metric: 'visits', target: 3, enabled: true, ...patch,
});

describe('Редактор достижений', () => {
  it('новое достижение получает свободный ключ', () => {
    expect(newAchievementKey([])).toBe('custom_1');
    expect(newAchievementKey([rule({ key: 'custom_2' })])).toBe('custom_3');
    expect(emptyAchievement([rule({ key: 'custom_2' })]).key).toBe('custom_3');
  });

  it('корректные правила проходят', () => {
    expect(validateAchievements([rule(), rule({ key: 'b', metric: 'podium', target: 10 })])).toBeNull();
  });

  it('ловит пустое название, условие и неверную цель', () => {
    expect(validateAchievements([rule({ title: ' ' })])).toContain('Название');
    expect(validateAchievements([rule({ description: '' })])).toContain('Условие');
    expect(validateAchievements([rule({ target: 0 })])).toContain('Цель');
    expect(validateAchievements([rule({ metric: 'podium', target: 51 })])).toContain('пьедестала');
  });

  it('не больше 20 достижений', () => {
    expect(validateAchievements(Array.from({ length: 21 }, (_, i) => rule({ key: `k${i}` })))).toContain('не больше 20');
  });
});
