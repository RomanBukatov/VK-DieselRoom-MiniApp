import React, { useCallback, useEffect, useState } from 'react';
import { Button, Empty, ErrorLine, Spinner, inputClass } from '../../components/ui';
import { adminApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { formatPermDateTime } from '../../utils/format';
import type { AdminTransaction } from '../../types';

const PAGE_SIZE = 100;

const OPERATION_LABELS: Record<string, string> = {
  VkLike: 'Лайк',
  VkComment: 'Комментарий',
  VkRepost: 'Репост',
  PollVote: 'Опрос',
  Visit: 'Визит по чеку',
  Referral: 'Бонус за друга',
  ProfileBonus: 'Бонус за профиль',
  GroupJoinBonus: 'Бонус за подписку',
  MessageAllowBonus: 'Бонус за сообщения',
  ShopPurchase: 'Обмен на подарок',
  ShopRefund: 'Возврат: отмена',
  ShopExpiredRefund: 'Возврат: истёк срок',
  ManualAdjust: 'Корректировка',
};

const signed = (n: number) => (n > 0 ? `+${n}` : String(n));

/** Журнал операций: все начисления и списания всех гостей, в том числе автоматические от ВКонтакте. */
export const OperationsLog: React.FC = () => {
  const [type, setType] = useState('');
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<AdminTransaction[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(async (append: boolean, beforeId?: number) => {
    try {
      const page = await adminApi.transactions({ type: type || undefined, q: q.trim() || undefined, beforeId });
      setRows((prev) => (append ? [...prev, ...page] : page));
      setHasMore(page.length >= PAGE_SIZE);
      setError(null);
      setLoaded(true);
    } catch (e) {
      setError(apiErrorMessage(e));
    }
  }, [type, q]);

  useEffect(() => {
    const id = setTimeout(() => void load(false), 300);
    return () => clearTimeout(id);
  }, [load]);

  return (
    <div className="space-y-2">
      <div className="grid grid-cols-2 gap-2">
        <select className={inputClass} value={type} aria-label="Тип операции" onChange={(e) => setType(e.target.value)}>
          <option value="">Все операции</option>
          {Object.entries(OPERATION_LABELS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <input className={inputClass} placeholder="Гость, карта, VK ID, № чека" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>
      <ErrorLine text={error} />
      {!loaded && !error && <div className="flex justify-center py-8"><Spinner /></div>}
      {loaded && rows.length === 0 && !error && <Empty>Операций нет.</Empty>}
      {rows.map((t) => (
        <div key={t.id} className="rounded-xl bg-dr-card border border-dr-border px-3 py-2 text-xs">
          <div className="flex justify-between gap-2">
            <b>{OPERATION_LABELS[t.type] ?? t.type}</b>
            <span className="text-dr-muted shrink-0">{formatPermDateTime(t.createdAt)}</span>
          </div>
          <div className="flex justify-between gap-2 mt-0.5">
            <div className="min-w-0">
              <div className="font-bold truncate">{t.guestName} <span className="font-normal text-dr-muted">· карта {t.cardNumber}</span></div>
              <div className="text-dr-muted">{t.description}</div>
            </div>
            <div className="text-right shrink-0 font-black">
              {t.coins !== 0 && <div className={t.coins < 0 ? 'text-dr-muted' : 'text-emerald-400'}>{signed(t.coins)}</div>}
              {t.rating !== 0 && <div className="text-dr-red-light">{signed(t.rating)} рейт.</div>}
            </div>
          </div>
        </div>
      ))}
      {hasMore && <Button variant="dark" className="w-full" onClick={() => load(true, rows[rows.length - 1]?.id)}>Показать ещё</Button>}
    </div>
  );
};
