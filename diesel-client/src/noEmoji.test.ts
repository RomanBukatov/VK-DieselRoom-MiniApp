import { describe, expect, it } from 'bun:test';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { ICONS, PRIZE_ICON_KEYS, MENU_ICON_KEYS, iconFor } from './config/icons';
import { Gift } from 'lucide-react';

// Эмодзи-пиктограммы (символы-картинки, вариационный селектор). Стрелки и типографские знаки — не эмодзи.
const EMOJI = /[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u{2B00}-\u{2BFF}\u{231A}\u{231B}\u{23E9}-\u{23FA}]|\u{FE0F}/u;

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : [];
  });
}

describe('Интерфейс без эмодзи', () => {
  it('ни в одном компоненте и модуле клиента нет эмодзи', () => {
    const offenders = sourceFiles(join(import.meta.dir))
      .flatMap((file) =>
        readFileSync(file, 'utf8')
          .split('\n')
          .map((line, i) => ({ file, line: i + 1, text: line }))
          .filter((l) => !l.text.trim().startsWith('//') && !l.text.trim().startsWith('*') && EMOJI.test(l.text)),
      )
      .map((l) => `${l.file}:${l.line}`);
    expect(offenders).toEqual([]);
  });

  it('стартовый экран index.html — без эмодзи, с логотипом', () => {
    const html = readFileSync(join(import.meta.dir, '..', 'index.html'), 'utf8');
    expect(EMOJI.test(html)).toBe(false);
    expect(html).toContain('/brand/logo.webp');
  });
});

describe('Векторные иконки', () => {
  it('все ключи для админки существуют', () => {
    for (const key of [...PRIZE_ICON_KEYS, ...MENU_ICON_KEYS]) expect(ICONS[key]).toBeDefined();
  });

  it('символ из старой базы показывается как подарок, а не как эмодзи', () => {
    expect(iconFor('☕')).toBe(Gift);
    expect(iconFor(null)).toBe(Gift);
    expect(iconFor('pizza')).toBe(ICONS.pizza);
  });
});
