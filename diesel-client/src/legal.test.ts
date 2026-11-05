import { describe, expect, it } from 'bun:test';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { PERSONAL_DATA_CONSENT, PROGRAM_RULES } from './config/legal';

const flatten = (sections: { title: string; paragraphs: string[] }[]) =>
  sections.map((s) => `${s.title} ${s.paragraphs.join(' ')}`).join(' ');

// Слова из проекта, на котором выросла программа, и из чужой предметной области
const LEGACY = /monte\s*carlo|poker|покер|турнир|бай-?ин|244-?фз|азартн/i;

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return files(path);
    return /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : [];
  });
}

describe('Юридические тексты', () => {
  const rules = flatten(PROGRAM_RULES);
  const consent = flatten(PERSONAL_DATA_CONSENT);

  it('программа лояльности бара-ресторана с адресом и возрастом 18+', () => {
    expect(rules).toContain('бар-ресторан «DIESEL ROOM»');
    expect(rules).toContain('Пермь');
    expect(rules).toContain('Куйбышева, 47');
    expect(rules).toContain('18 лет');
    expect(rules).toContain('«18+»');
  });

  it('согласие называет закон, оператора, данные, цели, срок и отзыв', () => {
    expect(consent).toContain('152-ФЗ');
    expect(consent).toContain('Куйбышева, 47');
    expect(consent).toContain('телефона');
    expect(consent).toContain('программе лояльности');
    expect(consent).toContain('Отозвать согласие');
  });

  it('в текстах нет следов другого проекта', () => {
    expect(LEGACY.test(rules)).toBe(false);
    expect(LEGACY.test(consent)).toBe(false);
  });
});

describe('Следы другого проекта', () => {
  it('нет ни в клиенте, ни в настройках репозитория', () => {
    const root = join(import.meta.dir, '..', '..');
    const targets = [
      ...files(import.meta.dir),
      ...['docker-compose.yml', '.env.example', '.gitattributes', 'README.md', 'SECURITY.md'].map((f) => join(root, f)),
      join(import.meta.dir, '..', 'index.html'),
      join(import.meta.dir, '..', 'nginx.conf'),
    ];
    const offenders = targets.filter((file) => LEGACY.test(readFileSync(file, 'utf8')));
    expect(offenders).toEqual([]);
  });
});
