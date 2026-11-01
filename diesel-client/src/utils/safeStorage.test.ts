import { describe, it, expect, beforeEach } from 'bun:test';
import { safeStorage, safeSessionStorage, migrateOrClearStaleAppCache, APP_CACHE_VERSION, APP_CACHE_VERSION_KEY } from './safeStorage';

describe('safeStorage', () => {
  beforeEach(() => {
    safeStorage.clear();
    safeSessionStorage.clear();
  });

  it('sets and gets items correctly', () => {
    safeStorage.setItem('test_key', 'test_val');
    expect(safeStorage.getItem('test_key')).toBe('test_val');
  });

  it('removes item correctly', () => {
    safeStorage.setItem('key_to_delete', '123');
    safeStorage.removeItem('key_to_delete');
    expect(safeStorage.getItem('key_to_delete')).toBeNull();
  });

  it('returns null for non-existent items', () => {
    expect(safeStorage.getItem('non_existent')).toBeNull();
  });

  it('gets all keys including memory store keys', () => {
    safeStorage.setItem('key1', 'val1');
    safeStorage.setItem('key2', 'val2');
    const keys = safeStorage.getAllKeys();
    expect(keys).toContain('key1');
    expect(keys).toContain('key2');
  });

  it('safeSessionStorage works correctly', () => {
    safeSessionStorage.setItem('session_key', 'abc');
    expect(safeSessionStorage.getItem('session_key')).toBe('abc');
    safeSessionStorage.removeItem('session_key');
    expect(safeSessionStorage.getItem('session_key')).toBeNull();
  });

  it('migrateOrClearStaleAppCache clears all diesel_cached_* when version is outdated or missing', () => {
    safeStorage.setItem('diesel_cached_shop_items', JSON.stringify([{ id: 1, title: 'Old Item' }]));
    safeStorage.setItem('diesel_cached_profile', JSON.stringify({ id: 10, nickname: 'Old' }));
    safeStorage.setItem('diesel_cached_leaderboard', JSON.stringify({ seasonItems: [] }));
    safeStorage.setItem('diesel_cached_schedule', JSON.stringify([{ id: 101, title: 'Old Tourney' }]));
    safeStorage.setItem('diesel_cached_banner', JSON.stringify({ id: 1, isEnabled: true }));
    safeStorage.setItem('unrelated_key', 'keep_me');
    safeStorage.setItem(APP_CACHE_VERSION_KEY, 'v0.0.1-old');

    migrateOrClearStaleAppCache();

    expect(safeStorage.getItem('diesel_cached_shop_items')).toBeNull();
    expect(safeStorage.getItem('diesel_cached_profile')).toBeNull();
    expect(safeStorage.getItem('diesel_cached_leaderboard')).toBeNull();
    expect(safeStorage.getItem('diesel_cached_schedule')).toBeNull();
    expect(safeStorage.getItem('diesel_cached_banner')).toBeNull();
    expect(safeStorage.getItem('unrelated_key')).toBe('keep_me');
    expect(safeStorage.getItem(APP_CACHE_VERSION_KEY)).toBe(APP_CACHE_VERSION);
  });

  it('migrateOrClearStaleAppCache preserves valid cache if version matches current', () => {
    safeStorage.setItem(APP_CACHE_VERSION_KEY, APP_CACHE_VERSION);
    safeStorage.setItem('diesel_cached_shop_items', JSON.stringify([{ id: 2, title: 'New Item' }]));

    migrateOrClearStaleAppCache();

    expect(safeStorage.getItem('diesel_cached_shop_items')).not.toBeNull();
  });
});
