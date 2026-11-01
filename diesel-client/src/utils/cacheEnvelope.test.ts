import { describe, it, expect, beforeEach } from 'bun:test';
import { readCache, writeCache } from './cacheEnvelope';
import { safeStorage, APP_CACHE_VERSION } from './safeStorage';

const KEY = 'diesel_cached_test_envelope';

describe('cacheEnvelope', () => {
  beforeEach(() => {
    safeStorage.removeItem(KEY);
  });

  it('writes {v, ts, data} and reads data back', () => {
    writeCache(KEY, [{ id: 1 }]);
    const raw = JSON.parse(safeStorage.getItem(KEY)!);
    expect(raw.v).toBe(APP_CACHE_VERSION);
    expect(typeof raw.ts).toBe('number');
    expect(readCache<{ id: number }[]>(KEY, 60_000)).toEqual([{ id: 1 }]);
  });

  it('discards and removes an entry written by another app version', () => {
    safeStorage.setItem(KEY, JSON.stringify({ v: 'v0.0.1', ts: Date.now(), data: [1] }));
    expect(readCache(KEY, 60_000)).toBeNull();
    expect(safeStorage.getItem(KEY)).toBeNull();
  });

  it('discards and removes an expired entry', () => {
    safeStorage.setItem(KEY, JSON.stringify({ v: APP_CACHE_VERSION, ts: Date.now() - 10_000, data: [1] }));
    expect(readCache(KEY, 5_000)).toBeNull();
    expect(safeStorage.getItem(KEY)).toBeNull();
  });

  it('discards legacy (non-envelope) arrays and corrupted JSON', () => {
    safeStorage.setItem(KEY, JSON.stringify([{ id: 1 }]));
    expect(readCache(KEY, 60_000)).toBeNull();
    expect(safeStorage.getItem(KEY)).toBeNull();

    safeStorage.setItem(KEY, '{not json');
    expect(readCache(KEY, 60_000)).toBeNull();
    expect(safeStorage.getItem(KEY)).toBeNull();
  });

  it('returns null when nothing is cached', () => {
    expect(readCache(KEY, 60_000)).toBeNull();
  });
});
