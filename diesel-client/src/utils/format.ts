// Форматирование дат и чисел. Заведение в Перми (UTC+5 без перехода на летнее время), поэтому все «местные»
// даты считаем от UTC со сдвигом, а не от часового пояса телефона гостя.

export const PERM_OFFSET_MS = 5 * 60 * 60 * 1000;

const pad = (n: number) => String(n).padStart(2, '0');

/** Разбор ISO-даты сервера: безопасно для старого iOS WebKit (строгий формат с 'T' и 'Z'). */
export function parseIso(value: string | null | undefined): number {
  if (!value) return NaN;
  const t = Date.parse(value);
  return Number.isFinite(t) ? t : NaN;
}

/** Местное время Перми как «поля» UTC-даты (getUTC* возвращают пермские значения). */
export function toPerm(utcMs: number): Date {
  return new Date(utcMs + PERM_OFFSET_MS);
}

export function permDateString(utcMs: number): string {
  const d = toPerm(utcMs);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

export function permTimeString(utcMs: number): string {
  const d = toPerm(utcMs);
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

export function formatPermDateTime(iso: string | null | undefined): string {
  const t = parseIso(iso);
  if (!Number.isFinite(t)) return '—';
  const d = toPerm(t);
  return `${pad(d.getUTCDate())}.${pad(d.getUTCMonth() + 1)} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

const WEEKDAYS = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];

/** «Пт, 07.11» из «2026-11-07». */
export function formatLocalDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  if (!y || !m || !d) return date;
  const weekday = WEEKDAYS[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
  return `${weekday}, ${pad(d)}.${pad(m)}`;
}

/** «21:30» из «21:30:00». */
export function formatLocalTime(time: string): string {
  return time.slice(0, 5);
}

export function plural(n: number, forms: [string, string, string]): string {
  const abs = Math.abs(n) % 100;
  const last = abs % 10;
  if (abs > 10 && abs < 20) return forms[2];
  if (last > 1 && last < 5) return forms[1];
  if (last === 1) return forms[0];
  return forms[2];
}

export function formatCoins(n: number): string {
  return `${n.toLocaleString('ru-RU')} ${plural(n, ['балл', 'балла', 'баллов'])}`;
}

/** Остаток времени: «6 д 23 ч», «3 ч 12 мин», «12 мин», «истёк». */
export function timeLeft(untilIso: string, nowMs: number): string {
  const until = parseIso(untilIso);
  if (!Number.isFinite(until)) return '—';
  const ms = until - nowMs;
  if (ms <= 0) return 'истёк';
  const totalMin = Math.floor(ms / 60_000);
  const days = Math.floor(totalMin / (60 * 24));
  const hours = Math.floor((totalMin % (60 * 24)) / 60);
  const minutes = totalMin % 60;
  if (days > 0) return `${days} д ${hours} ч`;
  if (hours > 0) return `${hours} ч ${minutes} мин`;
  return `${Math.max(minutes, 1)} мин`;
}

/** Живой отсчёт: «2 д 05:13:22», «05:13:22»; null — время вышло. */
export function raceCountdown(untilIso: string | null | undefined, nowMs: number): string | null {
  const until = parseIso(untilIso);
  if (!Number.isFinite(until) || until <= nowMs) return null;
  const total = Math.floor((until - nowMs) / 1000);
  const days = Math.floor(total / 86_400);
  const clock = `${pad(Math.floor((total % 86_400) / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
  return days > 0 ? `${days} д ${clock}` : clock;
}

export function formatPoints(n: number): string {
  return `${n.toLocaleString('ru-RU')} ${plural(n, ['очко', 'очка', 'очков'])}`;
}

/** Код привилегии для чтения вслух: «482 913». */
export function formatPrivilegeCode(code: string): string {
  return code.length === 6 ? `${code.slice(0, 3)} ${code.slice(3)}` : code;
}

/** «31.12.2026» из «2026-12-31». */
export function formatFullDate(date: string): string {
  const [y, m, d] = date.split('-');
  return y && m && d ? `${d}.${m}.${y}` : date;
}

export function formatPhone(digits: string | null | undefined): string {
  if (!digits || digits.length !== 11) return digits ?? '';
  return `+7 ${digits.slice(1, 4)} ${digits.slice(4, 7)}-${digits.slice(7, 9)}-${digits.slice(9)}`;
}
