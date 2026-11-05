import React, { useEffect, useState } from 'react';
import { Copy, Share2, Users } from 'lucide-react';
import { KeyIcon } from '../../components/Icon';
import { AntMascot } from '../../components/Brand';
import { Button, Card, Empty, ErrorLine, Field, Pill, Segmented, Sheet, inputClass } from '../../components/ui';
import { guestApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useSwr } from '../../store/useSwr';
import { useApp } from '../../store/useApp';
import { useNow } from '../../store/useNow';
import { bookingsTabOf, canGuestCancel, formatLabel, type BookingsTab } from '../../domain/booking';
import { buildReferralLink, referralRuleText, sanitizeReferralCode } from '../../utils/referral';
import { formatLocalDate, formatLocalTime, formatPermDateTime } from '../../utils/format';
import { copyTextToClipboard, shareLinkInVk } from '../../utils/vkBridge';
import type { Profile } from '../../types';

export const MyBookings: React.FC<{ version: number }> = ({ version }) => {
  const showToast = useApp((s) => s.showToast);
  const bookings = useSwr('diesel_cached_bookings', guestApi.myBookings, 60 * 60 * 1000);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [tab, setTab] = useState<BookingsTab>('active');
  const now = useNow(60_000);
  const reload = bookings.reload;

  useEffect(() => {
    if (version > 0) void reload();
  }, [version, reload]);

  const cancel = async (id: number) => {
    setBusyId(id);
    try {
      await guestApi.cancelBooking(id);
      showToast('Бронь отменена.');
      void bookings.reload();
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
    } finally {
      setBusyId(null);
    }
  };

  const all = bookings.data ?? [];
  const list = all.filter((b) => bookingsTabOf(b, now) === tab);
  const activeCount = all.filter((b) => bookingsTabOf(b, now) === 'active').length;
  return (
    <div className="space-y-2">
      <Segmented<BookingsTab>
        value={tab}
        onChange={setTab}
        options={[
          { value: 'active', label: `Активные${activeCount ? ` · ${activeCount}` : ''}` },
          { value: 'archive', label: 'Архив' },
        ]}
      />
      <ErrorLine text={!bookings.data ? bookings.error : null} onRetry={bookings.reload} />
      {list.length === 0 && <Empty>{tab === 'active' ? 'Активных броней нет. Забронируйте стол на вкладке «Афиша».' : 'В архиве пока пусто.'}</Empty>}
      {list.map((b) => (
        <Card key={b.id} className="!p-3">
          <div className="flex items-start justify-between gap-2">
            <div>
              <div className="text-sm font-bold">
                {formatLocalDate(b.date)} · {formatLocalTime(b.time)}
              </div>
              <div className="text-[11px] text-dr-muted">
                {formatLabel(b.format)} · гостей: {b.guestsCount}
              </div>
              {b.eventTitle && <div className="text-[11px] text-dr-red-light font-bold mt-0.5">{b.eventTitle}</div>}
              {b.staffComment && <div className="text-[11px] text-white/80 mt-0.5">Комментарий: {b.staffComment}</div>}
            </div>
            <Pill tone={b.status === 'Confirmed' ? 'green' : b.status === 'Pending' ? 'amber' : b.status === 'Rejected' ? 'red' : 'muted'}>
              {b.statusLabel}
            </Pill>
          </div>
          {canGuestCancel(b.status) && (
            <Button size="sm" variant="ghost" className="mt-1 !px-0" disabled={busyId === b.id} onClick={() => cancel(b.id)}>
              Отменить бронь
            </Button>
          )}
        </Card>
      ))}
    </div>
  );
};

export const ReferralCard: React.FC<{ profile: Profile }> = ({ profile }) => {
  const showToast = useApp((s) => s.showToast);
  const setProfile = useApp((s) => s.setProfile);
  const link = buildReferralLink(profile.referral.code);
  const r = profile.referral;
  const [code, setCode] = useState('');
  const [codeError, setCodeError] = useState<string | null>(null);
  const [applying, setApplying] = useState(false);

  const applyCode = async () => {
    setApplying(true);
    setCodeError(null);
    try {
      setProfile(await guestApi.applyReferral(sanitizeReferralCode(code)));
      setCode('');
      showToast('Код друга привязан. Он получит бонус после вашего первого визита.');
    } catch (e) {
      setCodeError(apiErrorMessage(e));
    } finally {
      setApplying(false);
    }
  };

  const share = async () => {
    const result = await shareLinkInVk(link);
    if (result === 'copied') showToast('Ссылка скопирована — отправьте её другу.');
    if (result === 'failed') showToast('Не удалось поделиться ссылкой.', 'error');
  };

  return (
    <Card watermark>
      <div className="text-sm font-black uppercase tracking-wide flex items-center gap-2">
        <Users className="w-4 h-4 text-dr-red-light" /> Приведи друга
      </div>
      <div className="text-xs text-dr-muted mt-1">{referralRuleText(r.rewardCoins, r.rewardRating)}</div>
      <div className="mt-3 flex items-center gap-2 bg-dr-surface border border-dr-border rounded-xl px-3 py-2">
        <span className="text-xs truncate flex-1">{link}</span>
        <button
          type="button"
          aria-label="Скопировать ссылку"
          onClick={async () => showToast((await copyTextToClipboard(link)) ? 'Ссылка скопирована.' : 'Не удалось скопировать.')}
        >
          <Copy className="w-4 h-4 text-dr-muted" />
        </button>
      </div>
      <div className="flex items-center justify-between mt-3">
        <div className="text-[11px] text-dr-muted">
          Приглашено: <b className="text-white">{r.invitedCount}</b> · бонус получен: <b className="text-white">{r.rewardedCount}</b>
        </div>
        <Button size="sm" onClick={share}>
          <Share2 className="w-3.5 h-3.5" /> Поделиться
        </Button>
      </div>
      {r.canEnterCode && (
        <div className="mt-3 pt-3 border-t border-dr-border">
          <Field label="Вас пригласил друг?" hint="Введите его код в первые сутки, до первого визита.">
            <div className="flex gap-2">
              <input
                className={inputClass}
                value={code}
                inputMode="numeric"
                maxLength={16}
                placeholder="Код друга, например 100123"
                onChange={(e) => setCode(e.target.value)}
              />
              <Button className="shrink-0 !px-3" disabled={applying || !code.trim()} onClick={applyCode}>Применить</Button>
            </div>
          </Field>
          <div className="mt-2"><ErrorLine text={codeError} /></div>
        </div>
      )}
    </Card>
  );
};

export const AchievementsGrid: React.FC<{ profile: Profile }> = ({ profile }) => (
  <div className="grid grid-cols-3 gap-2">
    {profile.achievements.map((a) => (
      <div
        key={a.key}
        className={`rounded-2xl border px-2 py-3 text-center ${a.unlocked ? 'bg-dr-red/10 border-dr-red/40' : 'bg-dr-card border-dr-border opacity-60'}`}
        title={a.description}
      >
        <div className={`mx-auto w-10 h-10 rounded-xl border flex items-center justify-center ${a.unlocked ? 'border-dr-red/50 bg-dr-red/15 text-dr-red-light' : 'border-dr-border text-dr-muted'}`}>
          {a.icon === 'ant' ? <AntMascot white={!a.unlocked} className="w-7" /> : <KeyIcon name={a.icon} className="w-5 h-5" />}
        </div>
        <div className="text-[11px] font-bold leading-tight mt-1">{a.title}</div>
        <div className="text-[10px] text-dr-muted mt-0.5">{a.unlocked ? 'получено' : `${a.progress} / ${a.target}`}</div>
      </div>
    ))}
  </div>
);

export const History: React.FC = () => {
  const tx = useSwr('diesel_cached_history', guestApi.transactions, 60 * 60 * 1000);
  if (!tx.data?.length) return <Empty>Начисления появятся после первой активности.</Empty>;
  return (
    <div className="space-y-1.5">
      {tx.data.map((t) => (
        <div key={t.id} className="flex items-center justify-between gap-2 text-xs bg-dr-card border border-dr-border rounded-xl px-3 py-2">
          <div className="min-w-0">
            <div className="truncate">{t.description}</div>
            <div className="text-[10px] text-dr-muted">{formatPermDateTime(t.createdAt)}</div>
          </div>
          <div className="text-right shrink-0">
            <div className={`font-black ${t.coins < 0 ? 'text-dr-muted' : 'text-emerald-400'}`}>{t.coins > 0 ? `+${t.coins}` : t.coins}</div>
            {t.rating !== 0 && <div className="text-[10px] text-dr-red-light">{t.rating > 0 ? `+${t.rating}` : t.rating} рейт.</div>}
          </div>
        </div>
      ))}
    </div>
  );
};

/** Вход персонала на общем планшете: ПИН-код сотрудника или мастер-ключ администратора. */
export const StaffLoginSheet: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const loginStaff = useApp((s) => s.loginStaff);
  const [pin, setPin] = useState('');
  const [masterKey, setMasterKey] = useState('');
  const [useMaster, setUseMaster] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    setBusy(true);
    setError(null);
    const problem = await loginStaff(useMaster ? '' : pin.trim(), useMaster ? masterKey.trim() : undefined);
    setBusy(false);
    if (problem) setError(problem);
    else {
      setPin('');
      setMasterKey('');
      onClose();
    }
  };

  return (
    <Sheet open={open} onClose={onClose} title="Вход для персонала">
      <div className="space-y-3 pb-3">
        {useMaster ? (
          <Field label="Мастер-ключ администратора">
            <input type="password" className={inputClass} value={masterKey} autoComplete="off" onChange={(e) => setMasterKey(e.target.value)} />
          </Field>
        ) : (
          <Field label="ПИН-код сотрудника" hint="4–8 цифр. Выдаёт администратор.">
            <input
              type="password"
              inputMode="numeric"
              autoComplete="off"
              className={`${inputClass} text-center tracking-[0.5em] text-xl`}
              maxLength={8}
              value={pin}
              onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))}
            />
          </Field>
        )}
        <ErrorLine text={error} />
        <Button className="w-full" disabled={busy || (useMaster ? masterKey.length < 12 : pin.length < 4)} onClick={submit}>
          {busy ? 'Проверяем…' : 'Войти'}
        </Button>
        <button type="button" className="w-full text-xs text-dr-muted" onClick={() => setUseMaster((v) => !v)}>
          {useMaster ? 'Войти по ПИН-коду сотрудника' : 'Войти по мастер-ключу администратора'}
        </button>
        <p className="text-[10px] text-dr-muted text-center">Вход действует до закрытия приложения.</p>
      </div>
    </Sheet>
  );
};
