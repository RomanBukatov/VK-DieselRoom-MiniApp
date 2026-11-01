// Кэш в localStorage в виде конверта {v, ts, data}: версия приложения + время записи.
// Устаревшие (по версии или возрасту) и повреждённые записи отбрасываются и удаляются.

import { APP_CACHE_VERSION, safeStorage } from './safeStorage';

interface CacheEnvelope<T> {
  v: string;
  ts: number;
  data: T;
}

export function writeCache<T>(key: string, data: T): void {
  try {
    const envelope: CacheEnvelope<T> = { v: APP_CACHE_VERSION, ts: Date.now(), data };
    safeStorage.setItem(key, JSON.stringify(envelope));
  } catch {
    // игнорируем: кэш — лишь ускоритель
  }
}

export function readCache<T>(key: string, maxAgeMs: number): T | null {
  try {
    const raw = safeStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<CacheEnvelope<T>> | null;
    if (
      !parsed ||
      typeof parsed !== 'object' ||
      Array.isArray(parsed) ||
      parsed.v !== APP_CACHE_VERSION ||
      typeof parsed.ts !== 'number' ||
      Date.now() - parsed.ts > maxAgeMs ||
      !('data' in parsed)
    ) {
      safeStorage.removeItem(key);
      return null;
    }
    return parsed.data as T;
  } catch {
    safeStorage.removeItem(key);
    return null;
  }
}
