// Отказоустойчивая обертка для localStorage и sessionStorage.
// Предотвращает фатальные крэши (DOMException: SecurityError / QuotaExceededError)
// в режимах Safari Incognito, приватных вкладках или строгих iframe-контейнерах VK/Telegram.

export const APP_CACHE_VERSION = 'v1.0.0';
export const APP_CACHE_VERSION_KEY = 'diesel_app_cache_version';

const memoryStorage: Record<string, string> = {};
const sessionMemoryStorage: Record<string, string> = {};

export const safeStorage = {
  getItem: (key: string): string | null => {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        return window.localStorage.getItem(key);
      }
    } catch {
      // Игнорируем исключения при блокировке storage браузером
    }
    return memoryStorage[key] ?? null;
  },

  setItem: (key: string, value: string): void => {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        window.localStorage.setItem(key, value);
        return;
      }
    } catch {
      // Игнорируем исключения при блокировке storage браузером
    }
    memoryStorage[key] = value;
  },

  removeItem: (key: string): void => {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        window.localStorage.removeItem(key);
      }
    } catch {
      // Игнорируем исключения при блокировке storage браузером
    }
    delete memoryStorage[key];
  },

  clear: (): void => {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        window.localStorage.clear();
      }
    } catch {
      // Игнорируем исключения при блокировке storage браузером
    }
    for (const key of Object.keys(memoryStorage)) {
      delete memoryStorage[key];
    }
  },

  getAllKeys: (): string[] => {
    const keys = new Set<string>();
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        for (let i = 0; i < window.localStorage.length; i++) {
          const k = window.localStorage.key(i);
          if (k) keys.add(k);
        }
      }
    } catch {
      // Игнорируем исключения
    }
    for (const k of Object.keys(memoryStorage)) {
      keys.add(k);
    }
    return Array.from(keys);
  }
};

export const safeSessionStorage = {
  getItem: (key: string): string | null => {
    try {
      if (typeof window !== 'undefined' && window.sessionStorage) {
        return window.sessionStorage.getItem(key);
      }
    } catch {
      // Игнорируем исключения
    }
    return sessionMemoryStorage[key] ?? null;
  },

  setItem: (key: string, value: string): void => {
    try {
      if (typeof window !== 'undefined' && window.sessionStorage) {
        window.sessionStorage.setItem(key, value);
        return;
      }
    } catch {
      // Игнорируем исключения
    }
    sessionMemoryStorage[key] = value;
  },

  removeItem: (key: string): void => {
    try {
      if (typeof window !== 'undefined' && window.sessionStorage) {
        window.sessionStorage.removeItem(key);
      }
    } catch {
      // Игнорируем исключения
    }
    delete sessionMemoryStorage[key];
  },

  clear: (): void => {
    try {
      if (typeof window !== 'undefined' && window.sessionStorage) {
        window.sessionStorage.clear();
      }
    } catch {
      // Игнорируем исключения
    }
    for (const key of Object.keys(sessionMemoryStorage)) {
      delete sessionMemoryStorage[key];
    }
  }
};

export const migrateOrClearStaleAppCache = (): void => {
  try {
    const currentVer = safeStorage.getItem(APP_CACHE_VERSION_KEY);
    if (currentVer !== APP_CACHE_VERSION) {
      const keysToPurge = [
        'diesel_cached_shop_items',
        'diesel_cached_profile',
        'diesel_cached_leaderboard',
        'diesel_cached_afisha',
        'diesel_cached_banner',
      ];
      for (const k of keysToPurge) {
        safeStorage.removeItem(k);
      }
      const allKeys = safeStorage.getAllKeys();
      for (const k of allKeys) {
        if (k && k.startsWith('diesel_cached_')) {
          safeStorage.removeItem(k);
        }
      }
      safeStorage.setItem(APP_CACHE_VERSION_KEY, APP_CACHE_VERSION);
    }
  } catch {
    // Игнорируем исключения
  }
};

