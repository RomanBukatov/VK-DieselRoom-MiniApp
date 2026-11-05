import { useEffect, useState } from 'react';
import { guestApi } from '../api/endpoints';
import type { PrivilegeCode } from '../types';

export interface LivePrivilegeCode extends PrivilegeCode {
  /** Момент обновления по часам телефона: часы гостя могут расходиться с сервером, поэтому считаем от secondsLeft. */
  refreshAt: number;
}

/** Временный код привилегии: обновляется, как только истекает минутное окно. */
export function usePrivilegeCode(enabled: boolean): LivePrivilegeCode | null {
  const [code, setCode] = useState<LivePrivilegeCode | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout>;

    const load = async () => {
      try {
        const fresh = await guestApi.privilegeCode();
        if (cancelled) return;
        setCode({ ...fresh, refreshAt: Date.now() + fresh.secondsLeft * 1000 });
        timer = setTimeout(load, fresh.secondsLeft * 1000 + 200);
      } catch {
        if (!cancelled) timer = setTimeout(load, 5000);
      }
    };
    void load();

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [enabled]);

  return enabled ? code : null;
}
