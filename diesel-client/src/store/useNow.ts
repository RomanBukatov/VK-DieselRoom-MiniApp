import { useEffect, useState } from 'react';

/** Текущее время, обновляемое по таймеру (для таймеров купонов и фильтров по дате без «нечистого» Date.now() в рендере). */
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
