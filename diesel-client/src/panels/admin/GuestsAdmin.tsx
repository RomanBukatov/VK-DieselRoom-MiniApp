import React, { useEffect, useState } from 'react';
import { Search } from 'lucide-react';
import { Button, Card, Empty, ErrorLine, Field, Pill, Sheet, Spinner, inputClass } from '../../components/ui';
import { adminApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useApp } from '../../store/useApp';
import { formatPermDateTime, formatPhone } from '../../utils/format';
import type { AdminGuest, PointsTransaction } from '../../types';

const PAGE_SIZE = 50;

const GuestAvatar: React.FC<{ url: string | null; name: string; size?: string }> = ({ url, name, size = 'w-10 h-10' }) =>
  url ? (
    <img src={url} alt="" className={`${size} rounded-full object-cover border border-dr-border shrink-0`} />
  ) : (
    <span className={`${size} rounded-full bg-dr-surface border border-dr-border flex items-center justify-center text-sm font-black text-dr-muted shrink-0`}>
      {name.slice(0, 1).toUpperCase()}
    </span>
  );

const parseDelta = (value: string): number => {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : 0;
};

/** Карточка гостя: данные, ручная корректировка с обязательной причиной и последние операции. */
const GuestSheet: React.FC<{ guest: AdminGuest; onClose: () => void; onChanged: (g: AdminGuest) => void }> = ({ guest, onClose, onChanged }) => {
  const showToast = useApp((s) => s.showToast);
  const [coins, setCoins] = useState('');
  const [rating, setRating] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [history, setHistory] = useState<PointsTransaction[] | null>(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let cancelled = false;
    adminApi.guestTransactions(guest.id).then(
      (list) => {
        if (!cancelled) setHistory(list);
      },
      () => {
        if (!cancelled) setHistory([]);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [guest.id, version]);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const updated = await adminApi.adjustGuest(guest.id, { coins: parseDelta(coins), rating: parseDelta(rating), reason });
      onChanged(updated);
      showToast('Корректировка записана в журнал.');
      setCoins('');
      setRating('');
      setReason('');
      setVersion((v) => v + 1);
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onClose={onClose} title="Гость">
      <div className="space-y-3 pb-3">
        <div className="flex items-center gap-3">
          <GuestAvatar url={guest.photoUrl} name={guest.name} size="w-16 h-16" />
          <div className="min-w-0">
            <div className="text-base font-black truncate">{guest.name}</div>
            <div className="text-[11px] text-dr-muted">
              Карта {guest.cardNumber} · VK {guest.vkId}{guest.phone ? ` · ${formatPhone(guest.phone)}` : ''}
            </div>
            <div className="mt-1 flex flex-wrap gap-1">
              <Pill tone="amber">{guest.levelTitle}</Pill>
              <Pill>{guest.visitsCount} визитов</Pill>
              {!guest.consentAcceptedAt && <Pill tone="red">нет согласия</Pill>}
            </div>
          </div>
        </div>

        <div className="grid grid-cols-3 gap-2 text-center">
          {[['Баллы', guest.coins], ['Неделя', guest.weeklyRating], ['Всего', guest.totalRating]].map(([label, value]) => (
            <div key={label} className="rounded-xl bg-dr-card border border-dr-border py-2">
              <div className="text-base font-black">{Number(value).toLocaleString('ru-RU')}</div>
              <div className="text-[10px] text-dr-muted">{label}</div>
            </div>
          ))}
        </div>

        <Card className="!p-3">
          <div className="text-xs font-bold uppercase text-dr-muted mb-2">Ручная корректировка</div>
          <div className="grid grid-cols-2 gap-2">
            <Field label="Баллы, + или −">
              <input className={inputClass} inputMode="text" placeholder="+50 или -50" value={coins} onChange={(e) => setCoins(e.target.value.replace(/[^\d+-]/g, ''))} />
            </Field>
            <Field label="Рейтинг, + или −">
              <input className={inputClass} inputMode="text" placeholder="+10 или -10" value={rating} onChange={(e) => setRating(e.target.value.replace(/[^\d+-]/g, ''))} />
            </Field>
          </div>
          <div className="mt-2">
            <Field label="Причина (обязательно)" hint="Гость увидит её в истории, администраторы — в журнале.">
              <input className={inputClass} maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} />
            </Field>
          </div>
          <div className="mt-2"><ErrorLine text={error} /></div>
          <Button className="w-full mt-2" disabled={busy || reason.trim().length < 3 || (parseDelta(coins) === 0 && parseDelta(rating) === 0)} onClick={submit}>
            {busy ? 'Сохраняем…' : 'Применить корректировку'}
          </Button>
        </Card>

        <div>
          <div className="text-xs font-bold uppercase text-dr-muted mb-1.5">Последние операции</div>
          {!history && <div className="flex justify-center py-4"><Spinner /></div>}
          {history?.length === 0 && <div className="text-xs text-dr-muted">Операций пока нет.</div>}
          <div className="space-y-1">
            {history?.map((t) => (
              <div key={t.id} className="flex items-center justify-between gap-2 text-[11px] bg-dr-card border border-dr-border rounded-lg px-2.5 py-1.5">
                <div className="min-w-0">
                  <div className="truncate">{t.description}</div>
                  <div className="text-dr-muted">{formatPermDateTime(t.createdAt)}</div>
                </div>
                <div className="text-right shrink-0 font-black">
                  {t.coins !== 0 && <div className={t.coins < 0 ? 'text-dr-muted' : 'text-emerald-400'}>{t.coins > 0 ? `+${t.coins}` : t.coins}</div>}
                  {t.rating !== 0 && <div className="text-dr-red-light">{t.rating > 0 ? `+${t.rating}` : t.rating} рейт.</div>}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </Sheet>
  );
};

/** Экран гостей: поиск по имени, карте, телефону или VK ID. */
export const GuestsAdmin: React.FC = () => {
  const [query, setQuery] = useState('');
  const [rows, setRows] = useState<AdminGuest[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [selected, setSelected] = useState<AdminGuest | null>(null);

  useEffect(() => {
    let cancelled = false;
    const id = setTimeout(() => {
      adminApi.guests(query.trim()).then(
        (page) => {
          if (cancelled) return;
          setRows(page);
          setHasMore(page.length >= PAGE_SIZE);
          setError(null);
        },
        (e: unknown) => {
          if (!cancelled) setError(apiErrorMessage(e));
        },
      );
    }, 300);
    return () => {
      cancelled = true;
      clearTimeout(id);
    };
  }, [query]);

  const loadMore = async () => {
    try {
      const page = await adminApi.guests(query.trim(), rows?.length ?? 0);
      setRows((current) => [...(current ?? []), ...page]);
      setHasMore(page.length >= PAGE_SIZE);
    } catch (e) {
      setError(apiErrorMessage(e));
    }
  };

  const onChanged = (updated: AdminGuest) => {
    setSelected(updated);
    setRows((current) => current?.map((g) => (g.id === updated.id ? updated : g)) ?? current);
  };

  return (
    <div className="space-y-2">
      <div className="relative">
        <Search className="w-4 h-4 text-dr-muted absolute left-3 top-1/2 -translate-y-1/2" />
        <input className={`${inputClass} !pl-9`} placeholder="Имя, номер карты, телефон или VK ID" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>
      <ErrorLine text={error} />
      {!rows && !error && <div className="flex justify-center py-8"><Spinner /></div>}
      {rows?.length === 0 && <Empty>Гости не найдены.</Empty>}
      <div className="grid gap-2 md:grid-cols-2">
        {rows?.map((g) => (
          <button key={g.id} type="button" className="text-left" onClick={() => setSelected(g)}>
            <Card className="!p-3">
              <div className="flex items-center gap-3">
                <GuestAvatar url={g.photoUrl} name={g.name} />
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-bold truncate">{g.name}</div>
                  <div className="text-[11px] text-dr-muted truncate">Карта {g.cardNumber} · {g.levelTitle} · {g.visitsCount} визитов</div>
                </div>
                <div className="text-right shrink-0">
                  <div className="text-sm font-black">{g.coins.toLocaleString('ru-RU')}</div>
                  <div className="text-[10px] text-dr-red-light font-bold">{g.weeklyRating} рейт.</div>
                </div>
              </div>
            </Card>
          </button>
        ))}
      </div>
      {hasMore && <Button variant="dark" className="w-full" onClick={loadMore}>Показать ещё</Button>}
      {selected && <GuestSheet key={selected.id} guest={selected} onClose={() => setSelected(null)} onChanged={onChanged} />}
    </div>
  );
};
