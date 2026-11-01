import React, { useEffect, useState } from 'react';
import { Check, Search, X } from 'lucide-react';
import { Button, Card, Empty, ErrorLine, Field, Pill, Spinner, inputClass } from '../../components/ui';
import { staffApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useApp } from '../../store/useApp';
import { useNow } from '../../store/useNow';
import { displayCode, orderStatusLabel } from '../../domain/orders';
import { formatLabel } from '../../domain/booking';
import { streakText, usageText } from '../../domain/privileges';
import { formatLocalDate, formatLocalTime, formatPermDateTime, formatPhone, timeLeft } from '../../utils/format';
import { triggerHaptic } from '../../utils/vkBridge';
import type { Booking, BookingStatus, GuestCard, StaffLookup, VisitType } from '../../types';

function useLookup() {
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<StaffLookup | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const search = async (q = query) => {
    if (q.trim().length < 2) return;
    setLoading(true);
    setError(null);
    try {
      setResult(await staffApi.lookup(q.trim()));
    } catch (e) {
      setError(apiErrorMessage(e));
      setResult(null);
    } finally {
      setLoading(false);
    }
  };
  return { query, setQuery, result, setResult, error, loading, search };
}

const SearchBox: React.FC<{ value: string; onChange: (v: string) => void; onSearch: () => void; placeholder: string; loading: boolean }> = ({
  value, onChange, onSearch, placeholder, loading,
}) => (
  <form
    className="flex gap-2"
    onSubmit={(e) => {
      e.preventDefault();
      onSearch();
    }}
  >
    <input className={inputClass} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} autoCapitalize="characters" />
    <Button type="submit" className="!px-3 shrink-0" disabled={loading} aria-label="Найти">
      {loading ? <Spinner className="w-4 h-4" /> : <Search className="w-4 h-4" />}
    </Button>
  </form>
);

const GuestHeader: React.FC<{ guest: GuestCard }> = ({ guest }) => (
  <div className="flex items-center justify-between gap-2">
    <div className="min-w-0">
      <div className="font-bold truncate">{guest.name}</div>
      <div className="text-[11px] text-dr-muted">
        Карта {guest.cardNumber}
        {guest.phone ? ` · ${formatPhone(guest.phone)}` : ''} · визитов: {guest.visitsCount}
      </div>
    </div>
    <div className="text-right shrink-0">
      <div className="text-sm font-black">{guest.coins}</div>
      <div className="text-[10px] text-dr-muted">баллов</div>
    </div>
  </div>
);

/** 🎁 Выдача подарка по купону #DR-XXXX или привилегии ранга с проверкой недельного лимита. */
export const IssueTool: React.FC = () => {
  const showToast = useApp((s) => s.showToast);
  const lookup = useLookup();
  const [busy, setBusy] = useState(false);
  const now = useNow();

  const issueCoupon = async (code: string) => {
    setBusy(true);
    try {
      await staffApi.issueCoupon(code);
      triggerHaptic('heavy');
      showToast(`✅ Купон ${displayCode(code)} погашен — подарок выдан.`);
      await lookup.search();
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
    } finally {
      setBusy(false);
    }
  };

  const redeem = async (guest: GuestCard) => {
    setBusy(true);
    try {
      const updated = await staffApi.redeemPrivilege(guest.userId);
      triggerHaptic('heavy');
      showToast(`✅ ${guest.privilege.label} — выдача записана в журнал.`);
      lookup.setResult((r) => (r ? { ...r, guests: r.guests.map((g) => (g.userId === updated.userId ? updated : g)) } : r));
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
      await lookup.search();
    } finally {
      setBusy(false);
    }
  };

  const order = lookup.result?.order;

  return (
    <div className="space-y-3">
      <SearchBox
        value={lookup.query}
        onChange={lookup.setQuery}
        onSearch={() => lookup.search()}
        loading={lookup.loading}
        placeholder="#DR-XXXX, номер карты, телефон или имя"
      />
      <ErrorLine text={lookup.error} />

      {order && (
        <Card glow={order.order.status === 'Pending'}>
          <div className="text-[11px] uppercase tracking-wider text-dr-muted">Подарок из магазина</div>
          <div className="text-lg font-black mt-1">
            {order.order.icon} {order.order.title}
          </div>
          <div className="text-xs text-dr-muted">
            {displayCode(order.order.code)} · {order.guestName} (карта {order.cardNumber})
          </div>
          <div className="mt-2 flex flex-wrap gap-1.5">
            <Pill tone={order.order.status === 'Pending' ? 'green' : 'muted'}>{orderStatusLabel(order.order, now)}</Pill>
            {order.order.status === 'Pending' && <Pill tone="amber">ещё {timeLeft(order.order.expiresAt, now)}</Pill>}
            {order.order.issuedAt && <Pill>выдан {formatPermDateTime(order.order.issuedAt)}</Pill>}
          </div>
          {order.order.status === 'Pending' && (
            <Button className="w-full mt-3" disabled={busy} onClick={() => issueCoupon(order.order.code)}>
              <Check className="w-4 h-4" /> Подтвердить выдачу
            </Button>
          )}
        </Card>
      )}

      {lookup.result && !order && lookup.result.guests.length === 0 && <Empty>Ничего не найдено.</Empty>}

      {lookup.result?.guests.map((g) => {
        const p = g.privilege;
        const streak = streakText(p);
        return (
          <Card key={g.userId}>
            <GuestHeader guest={g} />
            <div className="mt-3 rounded-xl bg-dr-surface border border-dr-border p-3">
              <div className="text-[11px] uppercase tracking-wider text-dr-muted">Привилегия ранга {p.rank ? `(#${p.rank})` : ''}</div>
              {p.label ? (
                <>
                  <div className="font-black mt-0.5">{p.label}</div>
                  <div className="text-[11px] text-dr-muted">{p.conditions} · {usageText(p)}</div>
                  {streak && <div className="text-[11px] text-amber-300 font-bold mt-0.5">{streak}</div>}
                  {!p.allowed && p.reason && <div className="text-[11px] text-dr-red-light mt-1">{p.reason}</div>}
                  <Button className="w-full mt-2" size="sm" disabled={busy || !p.allowed} onClick={() => redeem(g)}>
                    <Check className="w-4 h-4" /> Подтвердить выдачу
                  </Button>
                </>
              ) : (
                <div className="text-xs text-dr-muted mt-0.5">Нет активной привилегии — гость вне топ-50 недели.</div>
              )}
            </div>
          </Card>
        );
      })}
    </div>
  );
};

const VISIT_TYPES: { value: VisitType; label: string }[] = [
  { value: 'Regular', label: 'Обычный визит' },
  { value: 'BusinessLunch', label: 'Бизнес-ланч' },
  { value: 'PartyKaraoke', label: 'Вечеринка / Караоке' },
];

/** 🧾 Начисление за визит: гость + номер закрытого чека (1 чек = 1 раз) + тип визита. */
export const VisitTool: React.FC = () => {
  const showToast = useApp((s) => s.showToast);
  const lookup = useLookup();
  const [guest, setGuest] = useState<GuestCard | null>(null);
  const [receipt, setReceipt] = useState('');
  const [type, setType] = useState<VisitType>('Regular');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    if (!guest) return;
    setBusy(true);
    setError(null);
    try {
      const r = await staffApi.accrueVisit(guest.userId, receipt, type);
      triggerHaptic('heavy');
      showToast(
        `✅ Чек №${r.receiptNumber}: ${r.guest.name} +${r.coinsAwarded} баллов, +${r.ratingAwarded} в рейтинг` +
          (r.referralRewardPaid ? `. Бонус за друга получил ${r.inviterName}!` : '.'),
      );
      setGuest(r.guest);
      setReceipt('');
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      {!guest ? (
        <>
          <SearchBox
            value={lookup.query}
            onChange={lookup.setQuery}
            onSearch={() => lookup.search()}
            loading={lookup.loading}
            placeholder="Номер карты, телефон или имя гостя"
          />
          <ErrorLine text={lookup.error} />
          {lookup.result && lookup.result.guests.length === 0 && <Empty>Гость не найден.</Empty>}
          {lookup.result?.guests.map((g) => (
            <button key={g.userId} type="button" className="w-full text-left" onClick={() => setGuest(g)}>
              <Card className="!p-3">
                <GuestHeader guest={g} />
              </Card>
            </button>
          ))}
        </>
      ) : (
        <Card>
          <div className="flex items-start justify-between gap-2">
            <GuestHeader guest={guest} />
            <button type="button" onClick={() => setGuest(null)} aria-label="Сменить гостя" className="text-dr-muted">
              <X className="w-4 h-4" />
            </button>
          </div>
          <div className="mt-3 space-y-3">
            <Field label="Номер закрытого чека" hint="Один чек можно провести только один раз.">
              <input className={inputClass} value={receipt} maxLength={32} autoCapitalize="characters" onChange={(e) => setReceipt(e.target.value)} />
            </Field>
            <div className="grid grid-cols-3 gap-1.5">
              {VISIT_TYPES.map((v) => (
                <button
                  key={v.value}
                  type="button"
                  onClick={() => setType(v.value)}
                  className={`rounded-xl border px-2 py-2 text-[11px] font-bold ${type === v.value ? 'border-dr-red bg-dr-red/15' : 'border-dr-border text-dr-muted'}`}
                >
                  {v.label}
                </button>
              ))}
            </div>
            <ErrorLine text={error} />
            <Button className="w-full" disabled={busy || receipt.trim().length === 0} onClick={submit}>
              {busy ? 'Проводим…' : 'Начислить за визит'}
            </Button>
          </div>
        </Card>
      )}
    </div>
  );
};

/** 📅 Заявки на бронь с кнопками «✅ Подтвердить» и «✕ Отклонить». */
export const BookingsTool: React.FC = () => {
  const showToast = useApp((s) => s.showToast);
  const [filter, setFilter] = useState<BookingStatus | 'Active'>('Active');
  const [list, setList] = useState<Booking[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [comments, setComments] = useState<Record<number, string>>({});
  const [busyId, setBusyId] = useState<number | null>(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let cancelled = false;
    staffApi
      .bookings(filter === 'Active' ? undefined : filter)
      .then((data) => {
        if (!cancelled) {
          setList(data);
          setError(null);
        }
      })
      .catch((e) => !cancelled && setError(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [filter, version]);

  const act = async (id: number, action: 'confirm' | 'reject' | 'complete') => {
    setBusyId(id);
    try {
      if (action === 'confirm') await staffApi.confirmBooking(id, comments[id]);
      if (action === 'reject') await staffApi.rejectBooking(id, comments[id]);
      if (action === 'complete') await staffApi.completeBooking(id);
      showToast(action === 'confirm' ? '✅ Бронь подтверждена — гостю ушло сообщение.' : action === 'reject' ? '✕ Бронь отклонена — гостю ушло сообщение.' : 'Отмечено: гость посетил.');
      setVersion((v) => v + 1);
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
      setVersion((v) => v + 1);
    } finally {
      setBusyId(null);
    }
  };

  const filters: { value: BookingStatus | 'Active'; label: string }[] = [
    { value: 'Active', label: 'Активные' },
    { value: 'Pending', label: 'Ожидают' },
    { value: 'Completed', label: 'Посетили' },
    { value: 'Rejected', label: 'Отклонены' },
  ];

  return (
    <div className="space-y-2">
      <div className="flex gap-1.5 overflow-x-auto no-scrollbar">
        {filters.map((f) => (
          <button
            key={f.value}
            type="button"
            onClick={() => setFilter(f.value)}
            className={`shrink-0 rounded-full px-3 py-1 text-xs font-bold border ${filter === f.value ? 'bg-dr-red border-dr-red' : 'border-dr-border text-dr-muted'}`}
          >
            {f.label}
          </button>
        ))}
      </div>
      <ErrorLine text={error} onRetry={() => setVersion((v) => v + 1)} />
      {!list && !error && (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      )}
      {list?.length === 0 && <Empty>Заявок нет.</Empty>}
      {list?.map((b) => (
        <Card key={b.id} className="!p-3">
          <div className="flex items-start justify-between gap-2">
            <div>
              <div className="font-bold text-sm">
                {formatLocalDate(b.date)} · {formatLocalTime(b.time)} · {b.guestsCount} гост.
              </div>
              <div className="text-[11px] text-dr-muted">
                {formatLabel(b.format)} · {b.name} ·{' '}
                <a className="underline" href={`tel:+${b.phone}`}>
                  {formatPhone(b.phone)}
                </a>
              </div>
              {b.wishes && <div className="text-[11px] mt-0.5">💬 {b.wishes}</div>}
              {b.processedByStaffName && <div className="text-[10px] text-dr-muted mt-0.5">Обработал: {b.processedByStaffName}</div>}
            </div>
            <Pill tone={b.status === 'Confirmed' ? 'green' : b.status === 'Pending' ? 'amber' : 'muted'}>{b.statusLabel}</Pill>
          </div>
          {b.status === 'Pending' && (
            <>
              <input
                className={`${inputClass} mt-2 !py-2 text-xs`}
                placeholder="Комментарий гостю (необязательно): «Стол №5», «Зал занят»…"
                value={comments[b.id] ?? ''}
                maxLength={300}
                onChange={(e) => setComments((c) => ({ ...c, [b.id]: e.target.value }))}
              />
              <div className="grid grid-cols-2 gap-2 mt-2">
                <Button size="sm" disabled={busyId === b.id} onClick={() => act(b.id, 'confirm')}>
                  ✅ Подтвердить
                </Button>
                <Button size="sm" variant="dark" disabled={busyId === b.id} onClick={() => act(b.id, 'reject')}>
                  ✕ Отклонить
                </Button>
              </div>
            </>
          )}
          {b.status === 'Confirmed' && (
            <Button size="sm" variant="dark" className="w-full mt-2" disabled={busyId === b.id} onClick={() => act(b.id, 'complete')}>
              Гость пришёл
            </Button>
          )}
        </Card>
      ))}
    </div>
  );
};
