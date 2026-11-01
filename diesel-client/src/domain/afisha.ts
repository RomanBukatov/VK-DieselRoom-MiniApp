import type { MenuLink } from '../types';
import { parseIso } from '../utils/format';

/** Плитки «Меню»: активные и не скрытые по дате (банкетное меню — после 31.12.2026 23:59:59 по Перми). */
export function visibleMenu(menu: MenuLink[], nowMs: number): MenuLink[] {
  return menu
    .filter((m) => m.isActive && (!m.hiddenAfter || nowMs <= parseIso(m.hiddenAfter)))
    .sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);
}

/** Допустимая внешняя ссылка: только https (ссылки задаёт админ, но открываем их у гостя). */
export function isSafeUrl(url: string | null | undefined): url is string {
  return !!url && /^https:\/\//i.test(url.trim());
}
