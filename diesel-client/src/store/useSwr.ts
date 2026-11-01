import { useCallback, useEffect, useRef, useState } from 'react';
import { readCache, writeCache } from '../utils/cacheEnvelope';
import { apiErrorMessage } from '../api/client';

/**
 * Stale-while-revalidate: данные сразу из кэша localStorage (safeStorage, конверт с версией и временем),
 * затем свежие с сервера. Ключ null — запрос выключен.
 */
export function useSwr<T>(key: string | null, fetcher: () => Promise<T>, maxAgeMs = 24 * 60 * 60 * 1000) {
  const [data, setData] = useState<T | null>(() => (key ? readCache<T>(key, maxAgeMs) : null));
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(key !== null);
  const [version, setVersion] = useState(0);
  const fetcherRef = useRef(fetcher);
  useEffect(() => {
    fetcherRef.current = fetcher;
  });

  useEffect(() => {
    if (!key) return;
    let cancelled = false;
    fetcherRef.current().then(
      (fresh) => {
        if (cancelled) return;
        setData(fresh);
        setError(null);
        setLoading(false);
        writeCache(key, fresh);
      },
      (e: unknown) => {
        if (cancelled) return;
        setError(apiErrorMessage(e));
        setLoading(false);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [key, version]);

  /** Повторный запрос (кнопка «Повторить», обновление после действия). */
  const reload = useCallback(async () => {
    setLoading(true);
    setVersion((v) => v + 1);
  }, []);

  const mutate = useCallback(
    (next: T) => {
      setData(next);
      if (key) writeCache(key, next);
    },
    [key],
  );

  return { data, error, loading, reload, mutate };
}
