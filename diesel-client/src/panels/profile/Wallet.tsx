import React, { useState } from 'react';
import { Flame, QrCode as QrIcon } from 'lucide-react';
import { KeyIcon } from '../../components/Icon';
import { Card, Pill, Sheet } from '../../components/ui';
import { AntMascot, CoinIcon } from '../../components/Brand';
import { QrCode } from '../../components/QrCode';
import { STREAK_PRIZE, streakText, usageText } from '../../domain/privileges';
import type { Profile } from '../../types';

/** Кошелёк баллов, очки рейтинга и текущая привилегия по месту в «Гонке недели». */
export const WalletCard: React.FC<{ profile: Profile }> = ({ profile }) => {
  const [cardOpen, setCardOpen] = useState(false);
  const p = profile.privilege;
  const streak = streakText(p);

  return (
    <>
      <Card glow watermark className="dr-hatch">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            {profile.photoUrl ? (
              <img src={profile.photoUrl} alt="" className="w-12 h-12 rounded-full object-cover border-2 border-dr-red/60" />
            ) : (
              <span className="w-12 h-12 rounded-full bg-dr-surface border-2 border-dr-red/60 flex items-center justify-center">
                <AntMascot className="w-9" />
              </span>
            )}
            <div className="min-w-0">
              <div className="font-black text-base truncate">{profile.firstName} {profile.lastName}</div>
              <button type="button" onClick={() => setCardOpen(true)} className="text-[11px] text-dr-muted flex items-center gap-1">
                <QrIcon className="w-3 h-3" /> Карта гостя № {profile.cardNumber}
              </button>
            </div>
          </div>
        </div>

        <div className="mt-4 flex items-end gap-2">
          <CoinIcon className="w-7 h-7" />
          <div className="text-3xl font-black leading-none">{profile.coins.toLocaleString('ru-RU')}</div>
          <div className="text-xs text-dr-muted pb-0.5">баллов на подарки</div>
        </div>

        <div className="grid grid-cols-3 gap-2 mt-4">
          <Stat label="Неделя" value={profile.weeklyRating} sub={profile.weeklyRank ? `#${profile.weeklyRank}` : '—'} />
          <Stat label="За всё время" value={profile.totalRating} sub={profile.totalRank ? `#${profile.totalRank}` : '—'} />
          <Stat label="Визиты" value={profile.visitsCount} sub="по чекам" />
        </div>
        <p className="text-[10px] text-dr-muted mt-2">Покупка подарков списывает баллы, но не уменьшает очки рейтинга.</p>
      </Card>

      <Card className="mt-3">
        <div className="text-[11px] uppercase tracking-wider text-dr-muted">Привилегия недели</div>
        {p.label ? (
          <>
            <div className="text-base font-black mt-1 flex items-center gap-2">
              <span className="w-8 h-8 rounded-lg bg-dr-red/10 border border-dr-red/40 flex items-center justify-center text-dr-red-light shrink-0">
                <KeyIcon name={p.icon} className="w-4 h-4" />
              </span>
              {p.label}
            </div>
            <div className="text-xs text-dr-muted mt-0.5">
              {p.rank}-е место · {p.conditions}
            </div>
            <div className="flex flex-wrap gap-1.5 mt-2">
              <Pill tone={p.allowed ? 'green' : 'muted'}>{p.allowed ? 'Можно получить сейчас' : 'Сейчас недоступно'}</Pill>
              {usageText(p) && <Pill>{usageText(p)}</Pill>}
            </div>
            {!p.allowed && p.reason && <div className="text-[11px] text-dr-muted mt-1.5">{p.reason}</div>}
            {streak && (
              <div className="mt-2 text-xs font-bold text-amber-300 flex items-center gap-1">
                <Flame className="w-3.5 h-3.5 shrink-0" /> {streak} → спецприз «{STREAK_PRIZE}»
              </div>
            )}
            <div className="text-[11px] text-dr-muted mt-2">Назовите официанту номер карты — привилегия проверяется по текущему месту.</div>
          </>
        ) : (
          <div className="text-sm text-dr-muted mt-1">
            Войдите в топ-50 «Гонки недели», чтобы получить привилегию: от коктейля до бесплатного бизнес-ланча.
          </div>
        )}
      </Card>

      <Sheet open={cardOpen} onClose={() => setCardOpen(false)} title="Карта гостя">
        <div className="flex flex-col items-center gap-3 pb-4">
          <QrCode value={profile.cardNumber} size={200} label={`Карта гостя ${profile.cardNumber}`} />
          <div className="text-2xl font-black tracking-[0.2em]">{profile.cardNumber}</div>
          <div className="text-xs text-dr-muted text-center">Покажите официанту при закрытии чека — визит начислится на эту карту.</div>
        </div>
      </Sheet>
    </>
  );
};

const Stat: React.FC<{ label: string; value: number; sub: string }> = ({ label, value, sub }) => (
  <div className="rounded-xl bg-dr-surface border border-dr-border px-2.5 py-2">
    <div className="text-[10px] text-dr-muted">{label}</div>
    <div className="text-lg font-black leading-tight">{value.toLocaleString('ru-RU')}</div>
    <div className="text-[10px] text-dr-red-light font-bold">{sub}</div>
  </div>
);
